# Shaders

메인 프로젝트 `Assets/Models/Paint/`에 있는 페인트 셰이더 사본입니다. 이 레포는 `Scripts/` 폴더만 subtree로 가져오기 때문에 따로 복사해 두었습니다. 두 파일 모두 제가 작성했습니다.

| 파일 | 역할 | 사용하는 곳 |
|---|---|---|
| [`Paint/PaintBrush.shader`](Paint/PaintBrush.shader) | 표면 메시를 UV 위치에 펼쳐 그리면서 Pass 0은 브러시 중심과의 월드 거리로 마스크를 칠하거나 지우고, Pass 1은 `ddx`/`ddy`로 픽셀 하나가 덮는 월드 면적을 기록합니다 | `PaintSurfaceManager` (`CommandBuffer.DrawMesh`) |
| [`Paint/PaintOverlayURP.shader`](Paint/PaintOverlayURP.shader) | 원본 머테리얼은 그대로 두고, 같은 메시 위에 마스크를 페인트 색으로 덧그리는 투명 오버레이 | `PaintableSurface`가 런타임에 생성하는 오버레이 오브젝트 |
