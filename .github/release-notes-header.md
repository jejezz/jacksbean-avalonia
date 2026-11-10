## 설치 / Install

| OS | 받을 파일 / File |
|---|---|
| macOS (Apple Silicon, 11.0 이상) | `*-macos-arm64.dmg` — 열어서 앱을 Applications로 끌어다 놓으세요 |
| Windows 10/11 (x64) | `*-windows-x64-setup.exe` (또는 설치 없이 쓰는 `*-windows-x64.zip`) |
| Linux (x64) | `*-linux-x64.tar.gz` — 압축을 풀고 `./install.sh`를 실행하세요 (`--remove`로 제거) |

**Windows**: 설치 프로그램에 아직 코드 서명이 없어서 SmartScreen이 "Windows의 PC 보호" 창을 띄웁니다. **추가 정보 → 실행**을 누르면 설치가 진행됩니다.
The installer isn't code-signed yet, so SmartScreen shows "Windows protected your PC" — choose **More info → Run anyway**.

**macOS**: 앱이 아직 Apple 개발자 서명·공증을 받지 않아서, 처음 열 때 "확인되지 않은 개발자" 경고가 뜹니다. 앱을 **우클릭 → 열기**로 한 번 열거나, 터미널에서 `xattr -dr com.apple.quarantine "/Applications/Jack’s Bean.app"` 를 실행하세요.
The app isn't signed or notarized by Apple yet, so macOS warns about an "unidentified developer" the first time. **Right-click → Open** once, or run `xattr -dr com.apple.quarantine "/Applications/Jack’s Bean.app"`.

**Linux**: X11 또는 XWayland에서 실행됩니다. 트레이 아이콘은 데스크톱이 StatusNotifier(AppIndicator)를 지원해야 보입니다. 전역 단축키는 지원하지 않습니다.
Runs on X11 or XWayland. The tray icon needs a desktop with StatusNotifier (AppIndicator) support. Global shortcuts are not supported.

**처음 실행하기 / First run**: 독(화면 가장자리의 작은 원)에 파일·폴더·드라이브를 끌어다 놓으면 정책에 따라 앱으로 열어 줍니다. 규칙과 앱은 홈 창에서 편집합니다. 독은 트레이 아이콘이나 설정의 단축키(Ctrl+Alt+J, Windows)로 숨길 수 있습니다.
Drop files, folders or drives on the dock (the small circle at the screen edge) and it opens them with the app your rules choose. Edit rules and apps in the home window.

받은 파일은 `SHA256SUMS.txt`로 확인할 수 있습니다: `shasum -a 256 -c SHA256SUMS.txt --ignore-missing`
