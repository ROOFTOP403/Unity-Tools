# Resource Cleaner

선택한 Material, Mesh, Texture가 씬이나 프로젝트에서 사용 중인지 확인하고 머티리얼을 정리하는 도구입니다.

## 주요 기능

- 현재 열린 씬에서 Material과 Mesh 사용 여부 검사
- 지정한 폴더의 Prefab에서 Material과 Mesh 사용 여부 검사
- 지정한 폴더의 Material에서 Texture 사용 여부 검사
- 사용 중인 리소스의 참조 오브젝트와 경로 표시
- 선택한 Material의 텍스처 슬롯 비우기
- 선택한 Material을 현재 Shader의 기본값으로 초기화

## 사용법

1. Project 창에서 검사할 Material, Mesh 또는 Texture를 선택합니다.
2. `Tools > RoofTop Studio > Resource Cleaner`를 엽니다.
3. 리소스 종류와 검사 범위를 선택합니다.
4. `선택 리소스 검사`를 누릅니다.
5. `미사용 표시` 또는 `사용 중 표시`로 결과를 확인합니다.

Project 창에서 리소스를 우클릭한 뒤 `RoofTop Studio > Resource Cleaner` 메뉴를 사용하면 일부 검사를 바로 실행할 수 있습니다.

## 주의

Material의 텍스처를 비우거나 Shader 기본값으로 초기화하는 기능은 실제 에셋을 변경합니다. Undo를 지원하지만 실행 전에 대상이 맞는지 확인하세요.

Prefab 폴더와 Material 폴더 설정은 `ResourceCleanerSettings.json`에 저장됩니다.
