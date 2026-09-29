# MewUI.Geometry.Sample

`MewUI.Geometry`의 연산별 입력과 결과를 보여주는 독립 예제입니다.

```powershell
dotnet run --project samples/MewUI.Geometry.Sample/MewUI.Geometry.Sample.csproj
```

- 파란색은 기준 도형, 주황색은 두 번째 도형, 초록색은 경로나 영역의 연산 결과입니다.
- 점 포함 여부 예제에서는 보라색 점을 클릭하거나 드래그해 판정 위치를 바꿉니다.
- 포함·교차 관계 예제에서는 주황색 도형을, `Combine` 예제에서는 두 입력 도형을 드래그해 결과 변화를 봅니다.
- 면적 예제에서는 고리 크기 조절 막대로 채워진 면적의 변화를 봅니다.
- 경계, 평탄화, 확장, 윤곽 예제는 고정된 입력과 결과를 나란히 보여줍니다.
