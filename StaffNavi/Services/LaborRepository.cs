using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>労務資料作成支援システムのデータ保存先（JSON）</summary>
    public class LaborRepository(DataStore store)
    {
        private const string StaffFile = "staff.json";
        private const string BatchesFile = "batches.json";
        private const string CorrectionsFile = "corrections.json";
        private const string LogsFile = "logs.json";
        private const string SettingsFile = "settings.json";
        private const int MaxLogs = 3000;

        public bool IsInitialized => store.Exists(BatchesFile);

        public List<StaffMember> GetStaff() => store.Read(StaffFile, () => new List<StaffMember>());

        public List<ImportBatch> GetBatches() => store.Read(BatchesFile, () => new List<ImportBatch>());

        public ImportBatch? GetBatch(string id) => GetBatches().FirstOrDefault(b => b.Id == id);

        public BatchData GetBatchData(string id) => store.Read($"batches/{SafeId(id)}.json", () => new BatchData());

        public void AddBatch(ImportBatch batch, BatchData data)
        {
            store.Write($"batches/{SafeId(batch.Id)}.json", data);
            store.Update(BatchesFile, () => new List<ImportBatch>(), list => list.Add(batch));
            if (batch.Kind == ImportKind.Staff)
            {
                store.Write(StaffFile, data.Staff);
            }
        }

        public List<Correction> GetCorrections() => store.Read(CorrectionsFile, () => new List<Correction>());

        public void AddCorrection(Correction correction) =>
            store.Update(CorrectionsFile, () => new List<Correction>(), list => list.Add(correction));

        public bool RevokeCorrection(string id, string user, string reason, DateTimeOffset at)
        {
            var found = false;
            store.Update(CorrectionsFile, () => new List<Correction>(), list =>
            {
                var target = list.FirstOrDefault(c => c.Id == id && !c.Revoked);
                if (target != null)
                {
                    target.Revoked = true;
                    target.RevokedBy = user;
                    target.RevokedAt = at;
                    target.RevokeReason = reason;
                    found = true;
                }
            });
            return found;
        }

        public List<OperationLog> GetLogs() => store.Read(LogsFile, () => new List<OperationLog>());

        public void AddLog(string user, string category, string message, DateTimeOffset? at = null) =>
            store.Update(LogsFile, () => new List<OperationLog>(), list =>
            {
                list.Add(new OperationLog { At = at ?? Clock.Now, User = user, Category = category, Message = message });
                if (list.Count > MaxLogs)
                {
                    list.RemoveRange(0, list.Count - MaxLogs);
                }
            });

        public LaborSettings GetSettings() => store.Read(SettingsFile, () => new LaborSettings());

        public void SaveSettings(LaborSettings settings) => store.Write(SettingsFile, settings);

        public PendingImport? GetPending(string token) =>
            Guid.TryParseExact(token, "N", out _) ? store.Read<PendingImport?>($"pending/{token}.json", () => null) : null;

        public void SavePending(PendingImport pending) => store.Write($"pending/{pending.Token}.json", pending);

        public void DeletePending(string token)
        {
            if (Guid.TryParseExact(token, "N", out _))
            {
                store.Delete($"pending/{token}.json");
            }
        }

        public void Clear() => store.Clear();

        private static string SafeId(string id) =>
            id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? id : throw new ArgumentException("不正なIDです。", nameof(id));
    }
}
