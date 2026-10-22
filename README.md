# CamtranslatorXpraim

Бесплатное локальное приложение «iPhone → веб-камера Windows». Собственные Mac, платный Apple Developer и облачные видеосервисы не требуются. **Версия 0.1 — разработка; готовность к ежедневному использованию на iPhone пока не подтверждена.**

Компоненты: SwiftUI + AVFoundation + WebRTC iOS; WPF/.NET 10 + Edge WebView2 Windows; локальный HTTPS/WSS; WebRTC DTLS-SRTP; Safari как независимый резервный клиент; интеграция OBS WebSocket 5 + OBS Virtual Camera.

Начните с [быстрого запуска](docs/QUICKSTART.md). [Установка iPhone без Mac](docs/IOS-INSTALL.md), [техническая реализуемость и источники](docs/FEASIBILITY.md), [архитектура](docs/ARCHITECTURE.md), [фактические проверки и ограничения](docs/STATUS.md).

## Windows

После локальной сборки `dist/CamtranslatorXpraim-Setup.exe` устанавливает приложение для текущего пользователя. Самостоятельная среда .NET входит в сборку; бесплатный WebView2 Runtime и OBS устанавливаются отдельно, если отсутствуют. Установщик не меняет брандмауэр или доверенные корни и не запускает приложение скрыто.

Для разработчика: установите бесплатный .NET SDK 10, затем в корне репозитория:

```powershell
dotnet build apps/windows/PhoneCam.Desktop/PhoneCam.Desktop.csproj -c Release -m:1
dotnet build tests/transport/TransportTests.csproj -c Release -m:1
dotnet tests/transport/bin/Release/net10.0/TransportTests.dll
./scripts/build-windows.ps1
```

Проверка настоящего WebRTC между двумя локальными peer на синтетическом видео (без iPhone):

```powershell
dotnet build tests/integration/WebRtcSmoke.csproj -c Release -m:1
./tests/integration/bin/Release/net10.0-windows/WebRtcSmoke.exe
```

Результат записывается в `artifacts/webrtc-smoke.json`. Тест использует production-приёмник и signaling, декодирует кадры и проверяет запись WebM. Синтетические кадры присутствуют исключительно в тесте.

## iOS

Workflow `.github/workflows/build-ios.yml` генерирует проект Xcode и собирает arm64 IPA без подписи на macOS runner. WebRTC закреплён на 154.0.0. IPA нужно переподписать AltStore Classic; он не устанавливается просто копированием файла. В этой Windows-среде сборка Xcode не выполнялась.

## Границы версии

Функции не считаются проверенными только по наличию исходников. Пока нет результатов на iPhone XR, аппаратном USB, OBS/Discord/Zoom. Собственной виртуальной камеры и виртуального микрофона нет. Аудио воспроизводится в Windows и может записываться вместе с видео. Автоматического доверия ранее сопряжённому iPhone нет: каждый новый сеанс подтверждается явно.

Нет обязательной телеметрии, сервиса обновлений, аккаунта приложения или платного API. Доступ к интернету нужен для первоначального получения инструментов и зависимостей, облачной сборки iOS и подписи Apple ID в AltStore; само видео передаётся локально.
