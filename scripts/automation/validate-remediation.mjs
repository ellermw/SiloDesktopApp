#!/usr/bin/env node

import fs from "node:fs";
import process from "node:process";
import { pathToFileURL } from "node:url";

function output(name, value) {
  if (!process.env.GITHUB_OUTPUT) return;
  fs.appendFileSync(process.env.GITHUB_OUTPUT, `${name}=${String(value)}\n`);
}

export function validateRemediation(report, result) {
  if (!result || !["fixed", "no_changes", "blocked"].includes(result.status))
    throw new Error("remediation result has an invalid status");
  if (!Array.isArray(result.decisions))
    throw new Error("remediation result decisions must be an array");
  if (result.decisions.length !== report.findings.length)
    throw new Error("remediation result must contain one decision per finding");

  const expected = new Set(report.findings.map((finding) => finding.index));
  const seen = new Set();
  for (const decision of result.decisions) {
    if (!expected.has(decision.finding_index) || seen.has(decision.finding_index))
      throw new Error(`invalid or duplicate finding index ${decision.finding_index}`);
    seen.add(decision.finding_index);
    if (!decision.reason || !decision.evidence || !decision.validation)
      throw new Error(`finding ${decision.finding_index} lacks concrete disposition evidence`);
  }

  const fixedCount = result.decisions.filter((decision) => decision.disposition === "fixed").length;
  return { fixedCount, hasFix: fixedCount > 0 };
}

function main() {
  const [reportPath, resultPath] = process.argv.slice(2);
  if (!reportPath || !resultPath)
    throw new Error("usage: validate-remediation.mjs <review-report.json> <result.json>");
  const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));
  const result = JSON.parse(fs.readFileSync(resultPath, "utf8"));
  const validation = validateRemediation(report, result);
  output("fixed_count", validation.fixedCount);
  output("has_fix", validation.hasFix);
  process.stdout.write(`${JSON.stringify(validation)}\n`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
