#!/usr/bin/env python3
"""Print the slowest test cases from a TRX report and append a job summary."""

from collections import Counter
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


DURATION = re.compile(r"(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)\Z")


def element_name(element):
    return element.tag.rsplit("}", 1)[-1]


def seconds(value):
    match = DURATION.fullmatch(value)
    if not match:
        raise ValueError(f"Unexpected TRX duration: {value!r}")
    days, hours, minutes, last_seconds = match.groups()
    return (int(days or 0) * 86400 + int(hours) * 3600 +
            int(minutes) * 60 + float(last_seconds))


def markdown(value):
    return value.replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def read_results(path):
    root = ET.parse(path).getroot()
    classes = {}
    for test in root.iter():
        if element_name(test) != "UnitTest":
            continue
        method = next((child for child in test.iter()
                       if element_name(child) == "TestMethod"), None)
        if method is not None:
            classes[test.get("id")] = method.get("className", "")

    results = []
    for result in root.iter():
        if element_name(result) != "UnitTestResult":
            continue
        raw_duration = result.get("duration")
        if raw_duration is None:
            continue
        name = result.get("testName") or result.get("testId") or "(unnamed test)"
        class_name = classes.get(result.get("testId"), "")
        if class_name and not name.startswith(class_name + "."):
            name = f"{class_name}.{name}"
        results.append((seconds(raw_duration), result.get("outcome", "Unknown"), name))
    return results


def main():
    if len(sys.argv) != 3:
        print("Usage: trx_timing_summary.py RESULTS_DIRECTORY GITHUB_STEP_SUMMARY", file=sys.stderr)
        return 2

    files = sorted(Path(sys.argv[1]).rglob("*.trx"))
    if not files:
        print(f"::error::No TRX report found under {sys.argv[1]}", file=sys.stderr)
        return 1

    results = [result for path in files for result in read_results(path)]
    if not results:
        print("::error::TRX reports contain no timed test cases", file=sys.stderr)
        return 1

    counts = Counter(outcome for _, outcome, _ in results)
    count_text = ", ".join(f"{outcome}: {count}" for outcome, count in sorted(counts.items()))
    lines = [
        "## Backend main test timings",
        "",
        f"{len(results)} test cases ({count_text}). Sum of test durations: "
        f"{sum(duration for duration, _, _ in results):.1f} s.",
        "",
        "| Rank | Duration | Outcome | Test |",
        "| ---: | ---: | --- | --- |",
    ]
    for rank, (duration, outcome, name) in enumerate(sorted(results, reverse=True)[:20], 1):
        lines.append(f"| {rank} | {duration:.2f} s | {markdown(outcome)} | {markdown(name)} |")
    summary = "\n".join(lines) + "\n"
    print(summary)
    with Path(sys.argv[2]).open("a", encoding="utf-8") as output:
        output.write(summary)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ET.ParseError, ValueError) as error:
        print(f"::error::Unable to summarize TRX report: {error}", file=sys.stderr)
        sys.exit(1)
