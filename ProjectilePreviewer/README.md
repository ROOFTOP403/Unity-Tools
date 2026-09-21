# Projectile Previewer
![Prjectile Previewer](../Thumb/ProjectilePreviewer_Thumb.png)
DOTween으로 움직이는 투사체를 Play Mode에 들어가지 않고 Scene 뷰에서 미리 보는 도구입니다.

## 주요 기능

- `DOTweenPath` 경로 재생과 시간 슬라이더 탐색
- 재생, 일시 정지, 재시작, 정지 및 반복 재생
- Particle System과 Trail Renderer를 포함한 투사체 프리뷰
- 경로 샘플 표시
- `IProjectilePreviewTweenSource`를 이용한 사용자 정의 경로 지원

## 사용법

1. `DOTweenPath` 또는 `IProjectilePreviewTweenSource`가 있는 오브젝트를 선택합니다.
2. `Tools > RoofTop Studio > Projectile Previewer`를 엽니다.
3. `Use Selection`을 누른 뒤 `Start Preview`를 선택합니다.
4. 재생 버튼이나 `Time` 슬라이더로 원하는 구간을 확인합니다.
5. 작업이 끝나면 `Stop`을 눌러 프리뷰 상태를 정리합니다.

## 요구 사항

- DOTween Pro (`DOTweenPath` 포함)
