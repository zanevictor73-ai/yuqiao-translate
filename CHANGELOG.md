# Changelog / 更新

## 1.10.0 — English joins the conversation / 英文完整对话支持

- Choose English or Japanese in the main or floating input window. Chinese is translated and automatically filled into Claude in the selected language.
- English and Japanese replies are independently detected and translated into the Chinese reading panel. Your Chinese original remains in the conversation history.
- Switching languages cancels stale results and pending Enter requests, while keeping the Chinese draft and protecting manually edited Claude drafts.
- English filling no longer depends on a successful Japanese translation. Automatic filling never sends; sending remains an explicit user action.
- Remember the outgoing language locally. Keep Windows voice typing, independent panels, verified clearing and stable reading position.
- 224 deterministic checks, including 29 new English-flow checks. Real desktop demonstrations verified both language flows on Windows 11.

新增「发送语言」切换：中文可自动译成英文或日文填入 Claude，英日文回复自动在右侧回译为中文。切换时保留中文、取消过期结果与待发送请求；仍由用户决定发送。通用版功能不变，两版统一更新至 1.10.0。公开版继续排除自动启动、总控台和时区功能。

## 1.9.2 — First public release / 首个开源版本

- Claude edition: Chinese input automatically becomes a Japanese draft in Claude; Japanese replies are translated into the reading panel on the right.
- General edition: independent Chinese → Japanese + English and Japanese / English → Chinese translation.
- Keeps Windows voice typing, explicit user-controlled sending, verified draft clearing, independent dark floating panels and stable reply-reading position.
- Removes automatic startup, background launch watching, dashboard and time-zone functionality from the public distribution.
- Adds Chinese and English documentation, Windows build scripts, portable packages and 195 automated checks.

主要体验是 Claude 双向对话：中文输入自动译成日文填入，日文回复在右侧自动译成中文。另提供通用翻译版。保留系统听写、手动确认发送、发送后清空、独立悬浮窗和阅读位置修正；公开版排除自动启动、总控台和时区修改。
