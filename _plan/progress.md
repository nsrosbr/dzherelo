# Журнал

- 2026-11-07T17:01:14 T1 закрито: черга сходиться за 5 прогонів, 82 перевірки зелені.
- 2026-11-07T17:22:00 T2 закрито: Editions.Revalidate — нова редакція веде relations.wasRevisionOf, стара superseded; selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО».
- 2026-11-07T17:49:50 T3 закрито: Cursors — state/cursors.json зберігає точку (OAI resumptionToken, dspace/wp номер сторінки), перерваний прогін не перечитує пройдені сторінки; Cursors.SelfCheck 5 перевірок, selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО».

- 2026-11-07T18:12:08 T4 «Теми у вікні» — done. MainForm: вкладка «Теми» з покриттям тем (стан/кількість/найсвіжіший/підстава) поверх Topics.Compute; оновлення на старті й після прогону. build 0 err + selftest зелений.

- 2026-11-07T18:34:38 T5 «Секрети через DPAPI» — done. AppSettings.OpenRouterKey та логіни проксі читаються/пишуться через Secrets (DPAPI); [JsonIgnore] — ключ не потрапляє в settings.json. Тест: серіалізовані налаштування без OpenRouter-поля + round-trip через Secrets. build 0 err + selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО».
- 2026-11-07T18:58:00 T6 «Самолікування джерел зі staging» — done. AdapterHeal: кандидатний адаптер приймається лише за точним збігом набору URL (SameUrlSet, не «хоч один»); staging HealAsync проганяє кандидатів на відомих-добрих URL з базою +1 на перелишок; override kind і --revert-adapter живуть у state/adapters.json (sources.json лишається базовим). 11 перевірок SelfCheck зелених; build 0 errors + selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО».
- 2026-11-07T19:34:39 T7 «HTML→PDF стилем канону (без бланка)» — done. CanonPdf (Css/Document/ExtractText/RenderToPdfAsync): WebView2 PrintToPdfAsync замість DOCX/cairosvg, нуль python. Стиль = A4, поля 30/20/20/20, PT Sans 10,5, Montserrat капсом, акцент #F29222, лінійки 0,5/1/1,5pt, колонтитул «бібліотека · тема · дата · стор. N». У Harvester сторінка без PDF → SavePageAsCanonAsync (+ .source.html поруч, fallback на чистий HTML якщо WebView2 недоступний). build 0 err + selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО» (10 перевірок канону); --canon дає %PDF-1.4 40 КБ зі вбудованими PTSans/Montserrat.
- 2026-11-07T20:01:00 T8 «Встановлення й оновлення» — done. Реліз — один файл: IncludeNativeLibrariesForSelfExtract у csproj вшиває WebView2Loader.dll/native-бібліотеки в exe. Скрипти build/package/sign/publish/release.ps1 за зразком Valera; package.ps1 дає release/Dzherelo.exe (111 317 788 байт, 0 супутніх .dll) + latest.txt (4 рядки). build 0 err + selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО» на single-file exe; sign fail-closed (серт — T11), publish чекає repo+токен (T11).
- 2026-11-07T20:28:00 T9 «Інсталятор із передперевіркою системи» — done.
Setup.cs: `--setup` або запуск exe під іменем `Setup.exe` ведуть у Setup.Run: сканує WebView2 Runtime
(GUID {F3017226}, HKLM обидва views + HKCU) / .NET (self-contained, довідково) / права запису в
%LOCALAPPDATA% / вільне місце (≥1 ГБ); докочує WebView2 bootstrapper (fwlink 2124703) якщо немає; копіює себе в
%LOCALAPPDATA%\Dzherelo\app\<ver>\ і пише ARP (HKCU Uninstall; portable.txt пропускає реєстр).
Доказ: build 0 errors + selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО» (6 перевірок setup); живий --setup: WebView2
157.0.4341.42 / права OK / 48,5 ГБ OK / встановив app\2.0.0\Dzherelo.exe + 9 значень ARP; Setup.exe-by-name роутинг
підтверджено; package.ps1 кладе release\Setup.exe.

- 2026-11-07T20:53:04 T10 «Деінсталяція з опціями» — done.
Setup.Uninstall (`--uninstall` / `--uninstall --purge` / `--remove-library`): кліар ARP (HKCU Uninstall\Dzherelo),
прибирає app/<ver>, у purge — цілий %LOCALAPPDATA%\Dzherelo (налаштування+секрети) і журнали/кеш у бібліотеці,
а саму теку бібліотеки — лише за явним --remove-library (SafeToDelete відмовляється від кореня диска;
замкнені файли став на вилучення після перезапуску через MoveFileEx). Доказ: build 0 errors + selftest
«УСІ ПЕРЕВІРКИ ПРОЙДЕНО» (3 перевірки uninstall); живий --uninstall прибрав app.0.0\Dzherelo.exe й ARP,
лишив data/secrets.dat + 6 шрифтів; --purge прибрав %LOCALAPPDATA%\Dzherelo цілком; бібліотеку
без підтвердження не чіпав. Стан відновлено через --setup.

- 2026-11-07T21:17:44 T12 «Нативність Windows» — done.
Один екземпляр (Mutex «Dzherelo.SingleInstance» у Program.cs перед Application.Run: друга копія виходить
з повідомленням), прогрес у таскбарі (TaskbarProgress/ITaskbarList3 над ProgressBar, graceful-noop без
explorer), DPI PerMonitorV2 у csproj (150% і більше без розмиття), WinExe — жодного консольного вікна,
ярлики «Пуск»+робочий стіл через WScript.Shell у Setup.Run (і прибирання в Uninstall), drag&drop файлів
у вікно → Harvester.IngestLocal (FormatGate+sha+дедуп+каталог, джерело «вручну», method local_file).
Доказ: build 0 errors + selftest «УСІ ПЕРЕВІРКИ ПРОЙДЕНО» (4 перевірки ingest + setup: ярлик створюється).
