# Yuqiao · Quick start

Extract the entire folder, then open the executable. Requires Windows, .NET Framework 4.8 and internet access. Local machine validation: Windows 11 x64.

## Claude edition: the main experience

Open Yuqiao.Claude.exe and the Claude Windows desktop app. Select “Claude 客户端”, enable automatic translation and filling, then choose “悬浮模式” (floating mode).

1. Type Chinese in Yuqiao, or select a Chinese input method and click “语音输入” (Voice input).
2. Japanese is translated and filled into Claude automatically. Review it first; only your explicit Enter requests sending.
3. Japanese replies are translated automatically in the Chinese panel on the right. Your original Chinese question is retained, and reply updates do not force you to the end.

The Chinese draft clears after the corresponding sent message is confirmed. Focus normally returns to Claude after filling; click the Chinese editor to revise the source. IME candidate confirmation does not send. Shift + Enter inserts a line break.

Input, reading and navigation panels can be hidden independently. Sidebar, profile-menu and settings translations follow visible UI elements. If changing conversations pauses the connection, use “重新连接” (Reconnect).

## General edition

Open Yuqiao.General.exe. Choose Chinese → Japanese + English, or Japanese / English → Chinese. Translation starts after roughly 0.6 seconds of inactivity; completion time depends on the network. Enter inserts a line break, “清空” clears the text, and “复制” copies a result.

General does not read or fill other applications.

## Speech and networking

The voice button opens Windows + H voice typing. Windows handles recognition; stop it from the system panel. Availability depends on language, microphone and online speech settings.

No LLM API key is required. Ordinary machine translation sends text to Microsoft / Google, and Windows dictation may also use online services. This is not an offline or API-free application.

## Troubleshooting

- Cannot launch: verify Windows and .NET Framework 4.8. The project is unsigned; download from the project release page and verify SHA256.
- Translation failure: check your connection and retry. Public translation endpoints may be limited or changed.
- Claude disconnected: open the Windows desktop client, keep the conversation visible and reconnect.
- Filling paused: check for a manually edited draft or a changed conversation. Yuqiao preserves text it does not own.
- Web, macOS or other AI apps: this automatic conversation workflow is not claimed as supported. General remains available on Windows.

The open-source editions exclude automatic startup, background launch watching, a dashboard and time-zone changes.

Project and updates: https://github.com/zanevictor73-ai/yuqiao-translate
