<p align="center">
  <img src="images/logo.png" width="128" alt="">
</p>

<h1 align="center">YaMu+</h1>

<p align="center">
  Сохранение треков, альбомов и плейлистов из десктопной Яндекс Музыки в обычные файлы
</p>

<p align="center">
  <a href="https://github.com/YaMuPlus/YaMuPlus/releases/latest/download/YaMuPlus-windows-x64.exe"><img src="https://img.shields.io/badge/%D0%A1%D0%BA%D0%B0%D1%87%D0%B0%D1%82%D1%8C-Windows-FFFF00?style=for-the-badge&labelColor=1A1A1A" alt="Скачать для Windows"></a>
  <a href="https://github.com/YaMuPlus/YaMuPlus/releases/latest/download/YaMuPlus-macos-arm64.dmg"><img src="https://img.shields.io/badge/%D0%A1%D0%BA%D0%B0%D1%87%D0%B0%D1%82%D1%8C-macOS-FFFF00?style=for-the-badge&labelColor=1A1A1A" alt="Скачать для macOS"></a>
</p>

<p align="center">
  <b>Русский</b> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <img src="images/menu.png" alt="Меню альбома в Яндекс Музыке с пунктами «Сохранить локально» и «Удалить скачанные»">
</p>

## Возможности

- **Трек в файл.** «Сохранить локально» в меню ⋯ трека.
- **Альбом, сингл или плейлист в папку.** То же в меню ⋯ коллекции, по желанию с нумерацией треков.
- **Очистка.** «Удалить скачанные» убирает треки коллекции, скачанные в самой Музыке.

<table>
  <tr>
    <td width="50%"><img src="images/dialog.png" alt="Диалог сохранения: имя папки и место сохранения"></td>
    <td width="50%"><img src="images/progress.png" alt="Панель сохранения: идёт сохранение альбома, 4 из 14"></td>
  </tr>
  <tr>
    <td align="center">Имя папки и место сохранения</td>
    <td align="center">Ход сохранения прямо в Музыке</td>
  </tr>
</table>

## Установка

1. Скачайте YaMu+ кнопкой выше или из [Releases](https://github.com/YaMuPlus/YaMuPlus/releases). Версия для macOS — для Apple Silicon.
2. Установите Яндекс Музыку с официального сайта, если её ещё нет; версия из Microsoft Store не поддерживается.
3. macOS: откройте `YaMuPlus-macos-arm64.dmg` и перетащите YaMu+ в «Программы». YaMu+ не нотаризована Apple, поэтому в первый раз macOS её не откроет:
   - macOS 15 и новее — окно «Apple не удалось подтвердить, что файл „YaMu+“ не содержит вредоносного ПО…». Нажмите «Готово», откройте «Системные настройки» → «Конфиденциальность и безопасность», в разделе «Безопасность» нажмите «Все равно открыть» напротив YaMu+ и подтвердите паролем или Touch ID.
   - macOS 12–14 — окно «…невозможно открыть, так как Apple не может проверить его на наличие вредоносного ПО». Нажмите «ОК», затем в «Программах» щёлкните YaMu+ правой кнопкой, выберите «Открыть» и подтвердите.

   Дальше YaMu+ открывается как обычно. На любой версии вместо этого можно один раз выполнить в Терминале: `xattr -cr "/Applications/YaMu+.app"`.

## Запуск

Откройте YaMu+ и нажмите «Запустить»: Музыка откроется уже с новыми пунктами меню.

<p align="center">
  <img src="images/window.png" width="495" alt="Окно YaMu+">
</p>

<details>
<summary><b>Сборка из исходников</b></summary>

Любой из скриптов собирает оба релиза — `dist/YaMuPlus-windows-x64.exe` и `dist/YaMuPlus-macos-arm64.dmg`:

| Система | С установленным .NET SDK 9+ | Без .NET, в Docker |
|---|---|---|
| Windows | `powershell -ExecutionPolicy Bypass -File build.ps1` | `powershell -ExecutionPolicy Bypass -File build-docker.ps1` |
| macOS, Linux | `./build.sh` | `./build-docker.sh` |

Релиз собирает GitHub Actions тем же `build-docker.sh` (`.github/workflows/release.yml`): поднять `<Version>` в `src/YaMuPlus.csproj` и отправить тег с той же версией, например `v1.0.1`.

</details>
