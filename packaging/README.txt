JavMetaLite v{VERSION}
==================

[English]
1. Extract the archive and run JavMetaLite.exe; no separate .NET Runtime is required.
2. Choose or drop movies/folders. Use Batch workflow for multiple movies or recursive folder discovery.
3. Verify IDs and choose a source. LibreDMM is the recommended default; R18 may be slower due to rate limits. Custom queries both. JAVLibrary is manual web lookup only.
4. Review text, artwork, outputs and destination as needed. Opening every movie preview is optional.
5. Inspect the save preview and confirm any existing-metadata replacement. Movie saves run in order and stop on the first failure or cancellation.
6. "Preview not loaded" during batch saving is not a missing-output error. Without current-movie online sample candidates, "Replace local Extra Fanart" preserves local images.
7. Remove/Clear affects only the workspace. Confirmed local-image deletion in the viewer is immediate and is not undone by closing it.
The queue is memory-only. Finish needed saves before closing. Cancel through the task button, not Esc.
Back up important media. If recovery fails, keep the reported recovery folder and logs; do not delete it as cache.

[简体中文]
1. 解压后运行 JavMetaLite.exe；无需另装 .NET Runtime。
2. 选择或拖入影片／文件夹；多部影片或递归扫描可使用批量处理。
3. 检查番号与来源。默认推荐 LibreDMM；R18 可能因限流较慢；自定义会查询两者。JAVLibrary 仅手动网页查询。
4. 按需检查文字、图片、输出及目标位置，不必打开每部影片的预览。
5. 检查保存前预览并确认已有资料覆盖；影片逐部保存，首次失败或取消即停止。
6. 批量保存时“预览未加载”不代表输出缺失；没有当前影片的在线样张候选时，“替换本地 Extra Fanart”会保留本地图片。
7. 移除／清空仅影响工作区；查看器内确认删除本地图片会立即执行，关闭查看器不能撤销。
队列仅在内存中，关闭前完成需要的保存；通过任务按钮取消，Esc 不取消主界面任务。
请备份重要媒体；若恢复失败，请保留提示的恢复目录和日志，不要当作缓存删除。

[繁體中文]
1. 解壓縮後執行 JavMetaLite.exe；無需另裝 .NET Runtime。
2. 選取或拖入影片／資料夾；多部影片或遞迴掃描可使用批次處理。
3. 檢查番號與來源。預設建議 LibreDMM；R18 可能因限流較慢；自訂會查詢兩者。JAVLibrary 僅供手動網頁查詢。
4. 視需要檢查文字、圖片、輸出與目標位置，不必開啟每部影片的預覽。
5. 檢查儲存前預覽並確認已有資料覆寫；影片逐部儲存，首次失敗或取消即停止。
6. 批次儲存時「預覽未載入」不代表輸出缺失；沒有目前影片的線上樣張候選時，「替換本機 Extra Fanart」會保留本機圖片。
7. 移除／清空僅影響工作區；檢視器內確認刪除本機圖片會立即執行，關閉檢視器無法撤銷。
佇列僅在記憶體中，關閉前完成需要的儲存；透過任務按鈕取消，Esc 不會取消主介面任務。
請備份重要媒體；若復原失敗，請保留提示的復原目錄和記錄，不要當作快取刪除。

[日本語]
1. ZIP を展開して JavMetaLite.exe を実行します。.NET Runtime の別途インストールは不要です。
2. 動画やフォルダーを選択またはドロップします。複数動画や再帰スキャンには一括処理を使用できます。
3. 品番と取得元を確認します。標準は LibreDMM を推奨。R18 はレート制限で遅くなる場合があります。カスタムは両方に問い合わせます。JAVLibrary は手動 Web 検索のみです。
4. テキスト、画像、出力と保存先を必要に応じて確認します。全動画のプレビュー表示は不要です。
5. 保存前プレビューと既存情報の上書きを確認します。動画は順番に保存し、最初の失敗またはキャンセルで停止します。
6. 一括保存の「プレビュー未読み込み」は出力がないという意味ではありません。現在の動画にオンラインサンプル候補がなければ、「ローカル Extra Fanart を置換」でもローカル画像を保持します。
7. キューの削除／クリアは作業一覧のみを変更します。ビューアーで確認したローカル画像の削除は即時実行で、閉じても取り消せません。
キューはメモリ内のみです。必要な保存を終えてから終了してください。処理の中止はボタンで行い、メイン画面の Esc では中止しません。
重要な動画はバックアップしてください。復元失敗時は案内された復元フォルダーとログを保持し、キャッシュとして削除しないでください。

Windows 10/11 x64. Embedded browser / 内置浏览器 / 內建瀏覽器 / 内蔵ブラウザー:
Microsoft Edge WebView2 Runtime required.
Logs: %LOCALAPPDATA%\JavMetaLite\Logs (14-day retention by default).
Preferences: %LOCALAPPDATA%\JavMetaLite\settings.json
Project and source code: https://github.com/Noredge/JavMetaLite
License: MIT. See LICENSE.txt.
Third-party components: See THIRD_PARTY_NOTICES.txt.
