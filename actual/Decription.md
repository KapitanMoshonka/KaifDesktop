# KaifDesktop — этап беты: «Крутись» и захват мышки
This is one beta stage, not the completed full beta release.
Windows x64 / .NET Framework 4.8. Run KaifDesktop.exe; keep the entire folder together.
Frames/, memes/, meme-scales.json and data/settings.json are local to the executable.
MemeSourceDirectory defaults to memes; explicitly configured custom paths are preserved.
Normal settings/logs are in data/. Diagnostic reports, settings and logs are in tests/.
Tray/control window: pause, hide/show, return, seven actions, queue skip and exit.
The control window starts minimized. Calm click plays petting; drag is forbidden throughout
cursor/meme interactions. Escape releases the cursor. Close the control window to exit.
Spin action uses the user-approved v5 animation: 10 frames, 1150 ms per cycle;
TailCycles still sets the number of cycles (default 3). Its button is named "Крутись".
The checkbox "Включить захват мышки" defaults to enabled and is saved immediately.
Turning it off removes all waiting Cursor actions, disables manual cursor capture and
safely releases an active capture. Watch, petting and dragging the pet remain available.
Headless check: KaifDesktop.exe --self-test. No real cursor movement or UI.
UI, real mouse, monitors/DPI and visual acceptance need separate verification.
Known unresolved issue: reported minimized meme windows. This build does not fix it.
# Build verification
UTC: 2026-10-29T20:11:15.8909406Z
MSBuild: C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe
Release / net48 / C# 7.3 / x64 compilation and headless self-test passed.
Self-test working directory: TEMP. See tests/build.log and tests/self-test.txt.
Packaged 150 sprite frames and 9 meme files.
This build command runs no UI/cursor tests; separately executed UI checks are recorded in tests/.
Historical PASS is not new-machine UI validation.
Implemented: approved spin v5 and the persistent cursor-capture checkbox.
Other beta animation work and the minimized-meme fix are not implemented here.

# Проверки этапа беты «Крутись» и переключателя курсора

Интегрирован одобренный spin-beta/v5: 10 кадров 00..09, цикл 1150 мс, по умолчанию 3 цикла. В кнопке и очереди действие называется «Крутись»; внутренний ключ chase-tail сохранён.

«Включить захват мышки» включена по умолчанию, совместима со старыми JSON и сохраняет выбор сразу. Выключение удаляет все ожидающие Cursor, сохраняет допустимый порядок остальных, дополняет хвост с максимумом двух повторов и запрещает ручной запуск. Активный захват безопасно освобождается без возврата курсора в старую точку. Включение возвращает Cursor в пул; история выполнений сохраняется. Watch, поглаживание и перетаскивание питомца остаются доступны.

На окончательном actual/KaifDesktop.exe: Release/net48/x64, self-test PASS; native HWND/BM_CLICK проверка галочки и реальная layered-отрисовка всех 10 кадров за 3 цикла PASS. В диагностике использован только fake cursor, обычные data/settings.json не менялись. Core-тесты включают default/legacy true, загрузку false, сохранение, удаление/возврат Cursor, историю/max-two на 5000 переключениях, сохранность остальных сценариев, ручной запрет и освобождение активного захвата.

75-секундный UI smoke выполнен на кандидате того же исходного кода до окончательной сборки: PASS, GDI baseline/max/final=35/38/35, USER=48/48. Проверены прежние действия, пауза/скрытие, очередь/skip, сон/пробуждение, безопасный fake cursor и настоящее собственное окно мема. Протокол tests/pre-release-smoke-test.txt; завершающая сборка снова прошла self-test. Все 150 кадров поставки и 9 мемов совпали с источниками SHA-256. Точные сведения и StageId: tests/release-verification.json.

Снимок реального окна управления: tests/cursor-setting-controls.png. Первый UI-прогон обнаружил некорректное предположение теста о BM_GETCHECK, первый smoke сравнивал USER-ресурсы с открытым дополнительным окном автоматического мема. Исправлены тестовые проверки/очистка собственных окон, прежние допуски GDI/USER сохранены. Начальные протоколы и объяснения находятся в tests/diagnostic-history/; успешные результаты сохранены отдельно.

Это отдельный этап беты, а не завершённая полная бета. Художественная приёмка в приложении, аппаратная мышь, реальный захват курсора, смена DPI/мониторов и длительная стабильность требуют отдельной проверки. Прежний пользовательский дефект свёрнутых мемов этим этапом не исправлялся.

Обычный пользовательский экземпляр не запускался. StageId 0572da4db7fd4a27831e2c84b84bc0df остаётся Active: backup/version1 — проверенная копия прежней actual, backup/version2 — предыдущая резервная версия. Approve, commit и push не выполнялись.

## Сердечки поглаживания — 29 октября 2026
Подключены одобренные4 кадра: маленькие розовые сердечки поднимаются и исчезают при поглаживании. Собака, длительности и остальные функции сохранены. EXE и код не менялись, actual --self-test из TEMP PASS. Кадры SHA-256 совпадают с одобренным кандидатом devs/work/petting-hearts-beta. Направления ходьбы не добавлялись.
