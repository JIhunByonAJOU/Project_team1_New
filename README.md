# DRT Bus Dynamic Routing Simulation

| Scene View | Game View |
|---|---|
| ![Scene View](docs/assets/scene1.png) | ![Game View](docs/assets/game1.png) |

## 강화학습 캡스톤 프로젝트

**목표: 수요 기반 다음 정류장 선택으로 DRT 버스 운영 효율을 개선한다.**

- 문제: 고정 순환 노선은 실제 승객 수요와 무관하게 정류장을 방문한다.
- 해결: PPO agent가 현재 승객 상태를 보고 다음 정류장을 선택한다.
- 비교: `ONNX Inference` 정책 vs `Vanilla Sequential` baseline.
- 평가: service rate는 유지하고, wait time / episode time / distance / route legs / reward를 비교한다.

## MDP 관점 설계

| 구분 | 설계 |
|---|---|
| Agent | `DRTNextStopSelector` |
| Algorithm | PPO, Unity ML-Agents |
| Environment | Unity + Gley Traffic System |
| State: Bus | current stop, episode time, service rate |
| State: Capacity | waiting ratio, onboard ratio, remaining capacity ratio |
| State: Stop | valid flag, current-stop flag, travel feature |
| State: Demand | waiting passengers, dropoff targets, scheduled demand |
| State: Time | max wait time, max ride time |
| Action | next stop index |
| Action Mask | invalid stop, current stop |
| Training Policy | `MLAgentsTraining` |
| Inference Policy | `ONNXInference` |
| Baseline | `VanillaSequential` |
| Done | all requests completed, time limit, vehicle fault, invalid route |

## Reward / Penalty

```text
R_stop = boarding_reward + dropoff_reward - unboarded_passenger_penalty
```

| 항목 | 의미 |
|---|---|
| Boarding Reward | waiting passenger pickup |
| Dropoff Reward | onboard passenger completed |
| Waiting Penalty | unserved request waiting time |
| Failure Penalty | vehicle fault, route failure |

핵심: 가까운 정류장이 아니라 **대기 승객 + 하차 수요 + 서비스 완료**를 함께 고려하도록 학습한다.

## Passenger Request Panel

![Passenger request panel](docs/assets/passenger-request.png)

승객 요청 상태 확인용 패널.

- 승객 ID
- 출발 정류장과 도착 정류장
- 요청 발생 시간
- 현재 상태
- pickup/dropoff 시간
- 대기시간과 탑승시간

상태 흐름: `Scheduled` -> `Waiting` -> `OnBoard` -> `Completed`

## DRT Bus Status Panel

![DRT bus status panel](docs/assets/drt-bus-status.png)

버스 운행과 정책 실행 상태 확인용 패널.

- 실행 모드: `MatrixTeleport` 또는 `PhysicalDrive`
- 정책 모드: `MLAgentsTraining`, `ONNXInference`, `VanillaSequential`
- 현재 정류장과 마지막 처리 정류장
- episode 누적 주행거리
- 현재 assigned path 정보
- 탑승 중, 대기 중, 완료된 승객 수
- 정류장별 demand 상태

확인 포인트: 학습 정책이 단순 순환이 아니라 demand가 높은 정류장을 우선 선택하는가.

## 실험 결과 요약

기준: scenario 30, 12 stops, 30 passengers, `MatrixTeleport`, `DRTNextStopPPO-70999.onnx`.

| Metric           | Vanilla Sequential | ONNX Inference |      Change |
| ---------------- | -----------------: | -------------: | ----------: |
| Service rate     |              1.000 |          1.000 |         Tie |
| Episode distance |        43,424.14 m |    36,710.15 m |      -15.5% |
| Episode time     |         2,894.94 s |     2,447.34 s |      -15.5% |
| Average wait     |           340.16 s |       131.88 s |      -61.2% |
| P95 wait         |           750.39 s |       333.46 s |      -55.6% |
| Average ride     |           294.07 s |       216.14 s |      -26.5% |
| Route legs       |              62.00 |          51.50 |      -16.9% |
| Reward           |            -476.00 |         172.83 | ONNX better |

결론: service rate는 유지하면서 ONNX 정책이 대기시간, 운행시간, route leg, 이동거리를 줄였다.
