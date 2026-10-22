# Проверка технической реализуемости

Дата проверки источников: 22 октября 2026 года. Название согласовано: **CamtranslatorXpraim**. «Подтверждено документацией» не означает проверку на конкретном устройстве.

| Направление | Вывод и выбранное решение | Степень подтверждения |
|---|---|---|
| iPhone XR | Apple указывает 1080p 30/60. Реальные режимы каждой камеры запрашиваются через AVFoundation, не выводятся из модели телефона | Документация; устройство не подключено |
| Бесплатная iOS-сборка | Xcode запускается на macOS runner GitHub Actions. С Windows можно редактировать исходники, запускать workflow и получать unsigned IPA | Workflow подготовлен, облачный запуск не выполнен |
| GitHub Actions | Стандартные hosted runners бесплатны для публичных репозиториев. Private GitHub Free: 2000 минут/месяц и 500 МБ artifacts; macOS учитывается дороже. Larger runners платные | Проверено по GitHub Docs; не включать платные runners и бюджет перерасхода |
| Установка iOS | AltStore Classic + AltServer Windows; бесплатный Apple ID. Подпись истекает через 7 дней, активных sideload-приложений до трёх, включая AltStore | Официальная инструкция; установка здесь не проверена |
| Видеотранспорт | libwebrtc для iOS, WebRTC браузера в Windows; аппаратный H.264 VideoToolbox через стандартный encoder libwebrtc. Собственный RTP/UDP не нужен | Наличие API и бинарной зависимости проверено; аппаратный путь требует устройства |
| Windows-декодирование | WebView2 выбирает аппаратный декодер, если он доступен. Принудительная NVIDIA/CUDA не требуется. В диагностике доступно имя декодера, когда браузер сообщает его | Нельзя гарантировать аппаратное ускорение без измерений |
| Safari | getUserMedia требует безопасный контекст. Локальный CA + TLS с SAN для адресов ПК; CA нужно установить и явно доверить на iPhone | Код готов; Safari на устройстве не проверен |
| USB | Personal Hotspot по USB создаёт сетевой путь при наличии поддержки оператора и компонентов Apple. Используется тот же WebRTC. Детектор Apple-интерфейса не доказывает маршрут медиа | Требуется физический Lightning-кабель и iPhone |
| USB-туннелирование | libimobiledevice/usbmuxd — возможное отдельное исследование, но не прозрачная замена UDP ICE. Нужен туннель, анализ протокола и тесты. В текущую версию не включено | Не реализовано; не используется приватный API Apple |
| OBS | Window Capture отдельного окна видео → OBS Virtual Camera. WebSocket создаёт сцену, источник и включает камеру, затем спрашивает её статус | Код есть; OBS на машине не обнаружен, совместимость не подтверждена |
| Собственная камера | MFCreateVirtualCamera требует Windows build 22000. Для Windows 10 нужен другой источник, например COM DirectShow, со своими установкой и проверками | Отдельное направление; собственного драйвера/устройства нет |
| Виртуальный микрофон | OBS Virtual Camera переносит видео, не создаёт аудиоустройство. Бесплатный универсальный и проверенный собственный микрофон не реализован | Честное ограничение; сторонний аудиокабель не устанавливается |
| Фон iOS | Камера не обещается при блокировке/сворачивании. Приложение останавливает захват при уходе из активного состояния | Политика реализована, физический тест нужен |

## Источники

- [Apple: iPhone XR specifications](https://support.apple.com/en-us/111868).
- [Apple: AVCaptureDevice.formats](https://developer.apple.com/documentation/avfoundation/avcapturedevice/formats).
- [Apple: Personal Hotspot](https://support.apple.com/en-ie/111785).
- [GitHub Actions billing и бесплатные квоты](https://docs.github.com/en/billing/concepts/product-billing/github-actions).
- [AltStore: установка на Windows](https://faq.altstore.io/altstore-classic/how-to-install-altstore-windows).
- [AltStore: семь дней и три приложения](https://faq.altstore.io/altstore-classic/your-altstore).
- [stasel WebRTC M154: release, checksum и upstream commit](https://github.com/stasel/WebRTC/releases/tag/154.0.0).
- [W3C: Media Capture and Streams, secure context](https://www.w3.org/TR/mediacapture-streams/).
- [OBS Virtual Camera](https://obsproject.com/kb/virtual-camera-guide).
- [OBS WebSocket 5 protocol](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md).
- [Microsoft MFCreateVirtualCamera, minimum supported client](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera).

Зависимости бесплатны для использования, но имеют собственные лицензии. При распространении необходимо сохранять notices .NET, WebView2, WebRTC и его third-party components. Коммерческие облачные услуги не подключены.
