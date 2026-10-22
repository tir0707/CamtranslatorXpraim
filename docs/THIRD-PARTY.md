# Бесплатные зависимости и распространение

CamtranslatorXpraim не использует обязательных платных API. Отсутствие оплаты не отменяет лицензий компонентов.

- [.NET / ASP.NET Core / WPF](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT): MIT и third-party notices, включаемые в runtime distribution. При self-contained publish сохраняйте файлы лицензий из SDK/runtime.
- [Microsoft Edge WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3650.58): условия Microsoft WebView2 SDK/Runtime. Runtime распространяется Microsoft бесплатно; приложение использует установленный Evergreen Runtime.
- [WebRTC](https://webrtc.googlesource.com/src/+/refs/heads/main/LICENSE): BSD-style license и third-party компоненты. iOS binary distribution: [stasel/WebRTC 154.0.0](https://github.com/stasel/WebRTC/releases/tag/154.0.0). Перед публичным распространением IPA нужно включить полный набор notices выбранной сборки WebRTC.
- [OBS Studio](https://github.com/obsproject/obs-studio/blob/master/COPYING): GPL. OBS не встраивается и не перепаковывается в установщик; пользователь получает его с официального сайта.
- AltStore/AltServer, XcodeGen и GitHub Actions — отдельные инструменты со своими условиями. Apple ID используется только в официальном AltServer/AltStore.

Приложение и установщик Windows пока не имеют Authenticode-подписи издателя. Платный сертификат подписи не требуется для сборки; репутация SmartScreen и предупреждения Windows зависят от системы. Не отключайте защиту системы ради установки.
