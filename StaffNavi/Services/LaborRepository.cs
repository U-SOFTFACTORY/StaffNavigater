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
        private const string OrdersFile = "orders.json";
        private const string VersionFile = "version.json";
        private const string AdvancesFile = "advances.json";
        private const string AdvanceSettingsFile = "advance-settings.json";

        /// <summary>保存データの形式のバージョン（形式を変えたら上げる。古い形式のデータは起動時に作り直す）</summary>
        public const int DataVersion = 3;
        private const int MaxLogs = 3000;

        public bool IsInitialized => store.Exists(BatchesFile) && store.Read(VersionFile, () => 0) == DataVersion;

        public void MarkInitialized() => store.Write(VersionFile, DataVersion);

        public List<WorkOrder> GetOrders() => store.Read(OrdersFile, () => new List<WorkOrder>());

        public void SaveOrders(List<WorkOrder> orders) => store.Write(OrdersFile, orders);

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

        public List<AdvanceRequest> GetAdvances() => store.Read(AdvancesFile, () => new List<AdvanceRequest>());

        public void UpdateAdvances(Action<List<AdvanceRequest>> mutate) => store.Update(AdvancesFile, () => new List<AdvanceRequest>(), mutate);

        public AdvanceSettings GetAdvanceSettings() => store.Read(AdvanceSettingsFile, () => new AdvanceSettings());

        public void SaveAdvanceSettings(AdvanceSettings settings) => store.Write(AdvanceSettingsFile, settings);

        public void Clear() => store.Clear();

        private static string SafeId(string id) =>
            id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? id : throw new ArgumentException("不正なIDです。", nameof(id));
    }
}
