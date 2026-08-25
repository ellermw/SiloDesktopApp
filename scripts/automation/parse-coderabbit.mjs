#!/usr/bin/env node

import fs from "node:fs";
import process from "node:process";
import { pathToFileURL } from "node:url";

const VALID_SEVERITIES = new Set(["critical", "major", "minor", "trivial", "info", "none"]);
const NORMALIZED_SEVERITY = {
  critical: "critical",
  major: "major",
  minor: "minor",
  trivial: "minor",
  info: "minor",
  none: "minor",
};

function appendOutput(name, value) {
  if (!process.env.GITHUB_OUTPUT) return;
  fs.appendFileSync(process.env.GITHUB_OUTPUT, `${name}=${String(value)}\n`);
}

function neutralizeMentions(value) {
  return value.replaceAll("@", "@\u200b").replaceAll("```", "` ` `");
}

export function parseReview(text, metadata = {}) {
  const findings = [];
  let complete;

  for (const [offset, rawLine] of text.split(/\r?\n/u).entries()) {
    const line = rawLine.trim();
    if (!line) continue;

    let event;
    try {
      event = JSON.parse(line);
    } catch {
      throw new Error(`invalid CodeRabbit JSON on line ${offset + 1}`);
    }

    if (event.type === "error")
      throw new Error(`CodeRabbit error: ${event.message ?? event.error ?? "unknown error"}`);
    if (event.type === "finding") {
      const severity = String(event.severity ?? "").toLowerCase();
      if (!VALID_SEVERITIES.has(severity))
        throw new Error(`unsupported CodeRabbit severity: ${severity || "missing"}`);
      findings.push({
        index: findings.length + 1,
        severity: NORMALIZED_SEVERITY[severity],
        source_severity: severity,
        file: String(event.fileName ?? "unknown"),
        details: String(event.comment ?? event.codegenInstructions ?? "No details supplied."),
        remediation: String(event.codegenInstructions ?? event.comment ?? "Inspect manually."),
      });
    }
    if (event.type === "complete") complete = event;
  }

  if (!complete || !["review_completed", "review_skipped"].includes(complete.status))
    throw new Error("CodeRabbit review did not emit a successful completion event");
  const completionCount =
    complete.status === "review_skipped" && complete.findings == null ? 0 : Number(complete.findings);
  if (completionCount !== findings.length)
    throw new Error(
      `CodeRabbit completion count ${complete.findings} does not match ${findings.length} parsed findings`,
    );
  if (complete.status === "review_skipped" && findings.length !== 0)
    throw new Error("CodeRabbit cannot skip a review after emitting findings");

  const counts = { critical: 0, major: 0, minor: 0, total: findings.length };
  for (const finding of findings) counts[finding.severity] += 1;

  return {
    schema_version: 1,
    status: findings.length === 0 ? "clean" : "issues_found",
    repository: metadata.repository ?? "",
    pull_request: Number(metadata.pullRequest ?? 0),
    reviewed_sha: metadata.reviewedSha ?? "",
    base_sha: metadata.baseSha ?? "",
    cycle: Number(metadata.cycle ?? 0),
    generated_at: metadata.generatedAt ?? new Date().toISOString(),
    counts,
    findings,
    reviewed_files: Array.isArray(complete.reviewedFiles) ? complete.reviewedFiles : [],
  };
}

export function renderSummary(report) {
  const lines = [
    "<!-- silo-automated-review -->",
    "## Automated CodeRabbit review",
    "",
    `Reviewed \`${report.reviewed_sha}\` against \`${report.base_sha}\` (cycle ${report.cycle}/${5}).`,
    "",
    `**Result:** ${report.status === "clean" ? "clean" : `${report.counts.total} issue(s)`}`,
    "",
    "| Critical | Major | Minor |",
    "| ---: | ---: | ---: |",
    `| ${report.counts.critical} | ${report.counts.major} | ${report.counts.minor} |`,
  ];

  for (const finding of report.findings) {
    const details = neutralizeMentions(finding.details).slice(0, 4000);
    lines.push(
      "",
      `### ${finding.index}. ${finding.severity.toUpperCase()} — \`${finding.file.replaceAll("`", "\\`")}\``,
      "",
      ...details.split(/\r?\n/u).map((line) => `> ${line}`),
    );
  }

  lines.push(
    "",
    report.counts.critical > 0
      ? "Critical issues require owner review; automated remediation is stopped."
      : report.status === "clean"
        ? "No CodeRabbit issues remain for this commit."
        : "Validated findings are eligible for guarded Codex remediation after Windows validation passes.",
  );
  return `${lines.join("\n")}\n`;
}

function main() {
  const [inputPath, reportPath, summaryPath] = process.argv.slice(2);
  if (!inputPath || !reportPath || !summaryPath)
    throw new Error("usage: parse-coderabbit.mjs <input.ndjson> <report.json> <summary.md>");

  const report = parseReview(fs.readFileSync(inputPath, "utf8"), {
    repository: process.env.GITHUB_REPOSITORY,
    pullRequest: process.env.PR_NUMBER,
    reviewedSha: process.env.HEAD_SHA,
    baseSha: process.env.BASE_SHA,
    cycle: process.env.REMEDIATION_CYCLE,
  });
  fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`, { mode: 0o600 });
  fs.writeFileSync(summaryPath, renderSummary(report), { mode: 0o600 });

  appendOutput("issue_count", report.counts.total);
  appendOutput("critical_count", report.counts.critical);
  appendOutput("review_status", report.status);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
