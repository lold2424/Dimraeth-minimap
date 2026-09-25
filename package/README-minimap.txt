(English below / 영어 안내는 아래에 있습니다)

Dimraeth Minimap - 설치 안내
================================

화면 구석에 미니맵을 띄워 주는 모드입니다.
내 화면에만 그려지며, 게임 데이터나 네트워크에는 손대지 않습니다.
모드를 깐 사람과 안 깐 사람이 같이 플레이해도 됩니다.


설치 / 업데이트
---------------
1. 게임을 끕니다.
2. 받은 zip 을 아무 곳에나 통째로 풉니다. (바탕화면, 다운로드 폴더 등)
3. 풀린 폴더의 install.bat 을 더블클릭합니다.
   - Steam 에서 게임 위치를 자동으로 찾아 설치합니다. 다른 드라이브에 깔려 있어도 됩니다.
   - "Windows 의 PC 보호" 창이 뜨면 [추가 정보] > [실행] 을 누르세요.
   - 자동으로 못 찾으면 게임 폴더 주소를 물어봅니다.
     (Steam 라이브러리에서 Dimraeth 우클릭 > 관리 > 로컬 파일 보기 로 연 폴더)
4. 게임을 실행합니다.

새 버전이 나오면 새 zip 으로 같은 과정을 반복하면 됩니다. 설정은 그대로 유지됩니다.
설치가 끝나면 푼 폴더와 zip 은 지워도 됩니다.

   (수동 설치: payload 폴더 안의 내용물을 전부 게임 폴더에 복사해도 같습니다.)

   * 설치 후 첫 실행은 1~3분 걸리고 인터넷 연결이 필요합니다.
     (모드 로더가 게임 구조를 분석하는 과정이며, 두 번째부터는 평소와 같습니다.)
   * 백신이 winhttp.dll 을 막으면 예외로 등록해 주세요. BepInEx 모드 로더의 일부입니다.


조작
----
F6          본인 캐릭터 위의 체력 / 기력 / 집중력 게이지 켜기 / 끄기
            빨강 = 체력, 초록 = 기력, 파랑 = 집중력
            F7에서 게이지 크기, 높이, 투명도, 숫자 표시를 조절합니다.
            기력 표시 / 마력 표시도 각각 끌 수 있습니다. 숨긴 줄은 빈자리 없이 정리됩니다.
            미니맵을 꺼도 게이지는 표시됩니다. 기본값은 켜짐 / 숫자 숨김입니다.
F7          설정 창 열기 / 닫기
            키보드: 방향키 위/아래 = 항목 선택, 왼쪽/오른쪽 = 값 조절
            마우스: 값 옆의 < > 버튼 클릭, X = 닫기   (닫을 때 저장됩니다)
            미니맵 크기, 보이는 범위, 아이콘 크기, 위치, 간격, 투명도 등을 바로 바꿀 수 있습니다.
F8          미니맵 켜기 / 끄기
PageUp      확대
PageDown    축소
F9          진단 정보를 로그에 기록 (문제 제보용)

흰 점 = 나, 하늘색 점 = 같은 지역의 다른 플레이어
(범위 밖의 플레이어는 미니맵 테두리에 방향으로 표시됩니다.)

지도 화면과 같은 아이콘이 나옵니다: 발견한 장소, 퀘스트/대화 NPC, 지도 조각(먹은 것은 회색),
내가/파티가 찍은 표식, 추적 중인 퀘스트 목표(다이아몬드)와 길 안내 점선.
멀리 있는 퀘스트 목표와 표식은 테두리에 방향으로 붙습니다.
게임이 이미 보여 주는 정보만 표시하며, 몬스터나 숨겨진 것은 나오지 않습니다.


설정
----
게임을 한 번 실행하면 아래 파일이 생깁니다. 메모장으로 열어 고친 뒤 게임을 다시 켜면 됩니다.

  BepInEx\config\com.yhj.dimraeth.minimap.cfg

대부분은 게임 안 설정 창(F7)에서 바꿀 수 있습니다. 파일에서는 그 밖에 단축키와,
[Markers] 항목의 아이콘 종류별 켜기/끄기를 바꿀 수 있습니다.
프레임이 떨어지면 TextureSize 와 RefreshRate 를 낮춰 보세요.


게임이 업데이트된 뒤
--------------------
미니맵이 안 나오면 새 버전이 나올 때까지 기다려 주세요.
모드에 문제가 생겨도 미니맵만 꺼지고 게임은 정상 실행되도록 만들어져 있습니다.


제거
----
게임 폴더의 uninstall-minimap.bat 을 실행하면 모드와 모드 로더가 모두 지워집니다.
(Steam 의 "파일 무결성 검사"는 추가된 파일을 지워 주지 않습니다.)


포함된 것
---------
- Dimraeth Minimap (BepInEx\plugins\DimraethMinimap)
- BepInEx 6 (모드 로더, LGPL-2.1) - https://github.com/BepInEx/BepInEx
  라이선스 전문: licenses-minimap 폴더
게임 파일이나 게임에서 추출한 데이터는 들어 있지 않습니다.

======================================================================
ENGLISH
======================================================================

Dimraeth Minimap - install guide
================================

Adds a minimap in a screen corner.
It only draws on your own screen and never touches game data or networking.
You can play with people who do not have the mod.


Install / update
----------------
1. Close the game.
2. Extract the whole zip anywhere (Desktop, Downloads...).
3. Double-click install.bat in the extracted folder.
   - It finds the game through Steam and installs the mod, on any drive.
   - If "Windows protected your PC" appears, choose [More info] > [Run anyway].
   - If it cannot find the game, it asks for the game folder
     (Steam library: right-click Dimraeth > Manage > Browse local files).
4. Start the game.

For a new version, repeat the same steps with the new zip. Your settings are kept.
After installing you can delete the extracted folder and the zip.

   (Manual install: copy everything inside the payload folder into the game folder.)

   * The first launch after installing takes 1-3 minutes and needs an internet connection.
     (The mod loader analyses the game once; after that, launches are normal.)
   * If your antivirus blocks winhttp.dll, add an exception. It is part of the BepInEx mod loader.


Keys
----
F6          Show / hide bars above your own character
            Red = health, green = stamina, blue = concentration
            F7 adjusts bar size, vertical offset, opacity, and optional numbers.
            Show stamina / Show mana can be toggled separately; hidden rows take no space.
            Independent of minimap visibility. Enabled by default, numbers off.
F7          Open / close the settings panel
            Keyboard: Up/Down = select an item, Left/Right = change the value
            Mouse: click the < > buttons next to a value, X = close   (saved on close)
            Minimap size, visible range, icon size, position, margins, opacity, language...
F8          Show / hide the minimap
PageUp      Zoom in
PageDown    Zoom out
F9          Write diagnostics to the log (for bug reports)

White dot = you, light-blue dots = other players in the same region
(players out of range are shown on the rim, in their direction).

The minimap shows the same icons as the map screen: discovered places, quest / conversation NPCs,
map fragments (collected ones greyed out), beacons placed by you or your party, the targets of
tracked quests (diamond) and the dashed guidance routes.
Far quest targets and beacons stick to the rim as a direction hint.
Only what the game already shows you is displayed - no monsters, nothing hidden.


Settings
--------
Most settings are in the in-game panel (F7), including the language (Korean / English).
After the first launch this file also exists; edit it in Notepad and restart the game:

  BepInEx\config\com.yhj.dimraeth.minimap.cfg

It additionally holds the key bindings and, under [Markers], a switch per icon type.
If you lose FPS in camera mode, lower TextureSize and raise RefreshInterval (map mode, the default, costs nothing).


After a game update
-------------------
If the minimap stops showing, wait for a new version of the mod.
The mod is built so that a problem switches off the minimap only; the game keeps running.


Uninstall
---------
Run uninstall-minimap.bat in the game folder. It removes the mod and the mod loader.
(Steam's "Verify integrity of game files" does not remove added files.)


Contents
--------
- Dimraeth Minimap (BepInEx\plugins\DimraethMinimap)
- BepInEx 6 (mod loader, LGPL-2.1) - https://github.com/BepInEx/BepInEx
  License texts: licenses-minimap folder
No game files or game-derived data are included.
