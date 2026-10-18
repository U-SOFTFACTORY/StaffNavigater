// スマホ表示時のサイドメニュー開閉
document.addEventListener('click', function (e) {
  if (e.target.closest('[data-sidebar-open]')) {
    document.body.classList.add('sidebar-open');
  } else if (e.target.closest('[data-sidebar-close]')) {
    document.body.classList.remove('sidebar-open');
  }
});

// 確認ダイアログ付きのボタン・フォーム
document.addEventListener('submit', function (e) {
  var message = e.target.getAttribute('data-confirm');
  if (message && !window.confirm(message)) {
    e.preventDefault();
  }
});
