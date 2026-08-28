from __future__ import annotations

from dataclasses import dataclass

from .calibration import IsotonicCalibration
from .errors import BenchmarkError
from .scores import PairScore


@dataclass(frozen=True)
class EvaluatedPair:
    pair: PairScore
    normalized_percentage: float
    absolute_error: float | None
    within_tolerance: bool | None


def evaluate(
    rows: list[PairScore],
    calibration: IsotonicCalibration,
    tolerance: float,
) -> tuple[list[EvaluatedPair], dict[str, float]]:
    evaluation_rows = [row for row in rows if row.split == "evaluation"]
    evaluated: list[EvaluatedPair] = []
    for row in evaluation_rows:
        normalized = calibration.normalize(row.raw_score)
        error = (
            abs(normalized - row.target_percentage)
            if row.target_percentage is not None
            else None
        )
        evaluated.append(
            EvaluatedPair(
                pair=row,
                normalized_percentage=normalized,
                absolute_error=error,
                within_tolerance=error <= tolerance if error is not None else None,
            )
        )

    target_results = [item for item in evaluated if item.absolute_error is not None]
    if not target_results:
        raise BenchmarkError("Evaluation contains no target-bearing genuine pairs.")

    labels = [item.pair.same_identity for item in evaluated]
    scores = [item.normalized_percentage for item in evaluated]
    predictions = [score >= 50.0 for score in scores]
    true_positive = sum(label and prediction for label, prediction in zip(labels, predictions))
    false_negative = sum(label and not prediction for label, prediction in zip(labels, predictions))
    false_positive = sum(not label and prediction for label, prediction in zip(labels, predictions))
    true_negative = sum(not label and not prediction for label, prediction in zip(labels, predictions))

    metrics = {
        "meanAbsolutePercentageError": sum(
            item.absolute_error or 0.0 for item in target_results
        )
        / len(target_results),
        "withinToleranceRate": sum(item.within_tolerance is True for item in target_results)
        / len(target_results),
        "identityAccuracy": (true_positive + true_negative) / len(evaluated),
        "rocAuc": roc_auc(labels, scores),
        "falseAcceptRate": _safe_rate(false_positive, false_positive + true_negative),
        "falseRejectRate": _safe_rate(false_negative, false_negative + true_positive),
        "tarAtFar0.01": tar_at_far(labels, scores, 0.01),
        "tarAtFar0.001": tar_at_far(labels, scores, 0.001),
    }
    return evaluated, metrics


def roc_auc(labels: list[bool], scores: list[float]) -> float:
    positives = sum(labels)
    negatives = len(labels) - positives
    if positives == 0 or negatives == 0:
        raise BenchmarkError("ROC AUC requires both genuine and impostor evaluation pairs.")

    wins = 0.0
    for positive_score in (
        score for label, score in zip(labels, scores) if label
    ):
        for negative_score in (
            score for label, score in zip(labels, scores) if not label
        ):
            if positive_score > negative_score:
                wins += 1.0
            elif positive_score == negative_score:
                wins += 0.5
    return wins / (positives * negatives)


def tar_at_far(labels: list[bool], scores: list[float], maximum_far: float) -> float:
    impostor_scores = [score for label, score in zip(labels, scores) if not label]
    genuine_scores = [score for label, score in zip(labels, scores) if label]
    if not impostor_scores or not genuine_scores:
        raise BenchmarkError("TAR at FAR requires genuine and impostor pairs.")

    best_tar = 0.0
    thresholds = [float("inf"), *sorted(set(scores), reverse=True)]
    for threshold in thresholds:
        far = (
            sum(score >= threshold for score in impostor_scores)
            / len(impostor_scores)
        )
        if far <= maximum_far:
            tar = (
                sum(score >= threshold for score in genuine_scores)
                / len(genuine_scores)
            )
            best_tar = max(best_tar, tar)
    return best_tar


def _safe_rate(numerator: int, denominator: int) -> float:
    if denominator == 0:
        raise BenchmarkError("Metric denominator cannot be zero.")
    return numerator / denominator
