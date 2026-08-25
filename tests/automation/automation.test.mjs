import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";

import {
  MAX_REMEDIATION_CYCLES,
  evaluateContext,
} from "../../scripts/automation/guard-context.mjs";
import { parseReview, renderSummary } from "../../scripts/automation/parse-coderabbit.mjs";
import { validateRemediation } from "../../scripts/automation/validate-remediation.mjs";

const fixtureDirectory = path.join(path.dirname(fileURLToPath(import.meta.url)), "fixtures");
const repositoryRoot = path.resolve(fixtureDirectory, "..", "..", "..");
const fixture = (name) => fs.readFileSync(path.join(fixtureDirectory, name), "utf8");

const validContext = {
  repository: "ellermw/SiloDesktopApp",
  headRepository: "ellermw/SiloDesktopApp",
  baseRef: "main",
  headRef: "release/example",
  actor: "ellermw",
  cycle: 0,
  expectedHeadSha: "abc",
  currentHeadSha: "abc",
};

test("accepts a trusted same-repository pull request", () => {
  const result = evaluateContext(validContext);
  assert.equal(result.eligible, true);
  assert.equal(result.canRemediate, true);
});

test("rejects a fork pull request", () => {
  const result = evaluateContext({ ...validContext, headRepository: "attacker/fork" });
  assert.equal(result.eligible, false);
  assert.match(result.reasons.join(" "), /fork/u);
});

test("rejects a stale reviewed SHA", () => {
  const result = evaluateContext({ ...validContext, currentHeadSha: "new" });
  assert.equal(result.eligible, false);
  assert.match(result.reasons.join(" "), /stale/u);
});

test("rejects a missing expected head SHA", () => {
  const result = evaluateContext({ ...validContext, expectedHeadSha: "" });
  assert.equal(result.eligible, false);
  assert.match(result.reasons.join(" "), /expected head SHA/u);
});

test("permits the fifth review but prevents a sixth remediation", () => {
  const result = evaluateContext({ ...validContext, cycle: MAX_REMEDIATION_CYCLES });
  assert.equal(result.eligible, true);
  assert.equal(result.canRemediate, false);
});

test("prevents the automation PR from modifying itself", () => {
  const result = evaluateContext({
    ...validContext,
    headRef: "automation/review-remediation-loop",
  });
  assert.equal(result.eligible, true);
  assert.equal(result.canRemediate, false);
});

test("parses a clean CodeRabbit review", () => {
  const report = parseReview(fixture("clean.ndjson"), {
    reviewedSha: "head",
    baseSha: "base",
    generatedAt: "2026-08-25T00:00:00.000Z",
  });
  assert.equal(report.status, "clean");
  assert.equal(report.counts.total, 0);
  assert.match(renderSummary(report), /No CodeRabbit issues remain/u);
});

test("accepts an explicitly skipped review as a clean result", () => {
  const report = parseReview(fixture("skipped.ndjson"));
  assert.equal(report.status, "clean");
  assert.equal(report.counts.total, 0);
});

test("normalizes documented low-priority severities into the minor group", () => {
  const report = parseReview(fixture("extended-severities.ndjson"));
  assert.equal(report.counts.minor, 3);
  assert.deepEqual(
    report.findings.map((finding) => finding.source_severity),
    ["trivial", "info", "none"],
  );
});

test("groups CodeRabbit issues by severity", () => {
  const report = parseReview(fixture("findings.ndjson"));
  assert.deepEqual(report.counts, { critical: 1, major: 1, minor: 1, total: 3 });
  assert.equal(report.findings[1].file, "src/major.cs");
});

test("rejects malformed and failed CodeRabbit output", () => {
  assert.throws(() => parseReview(fixture("malformed.ndjson")), /invalid CodeRabbit JSON/u);
  assert.throws(() => parseReview(fixture("error.ndjson")), /authentication failed/u);
});

test("requires one remediation decision per finding", () => {
  const report = parseReview(fixture("findings.ndjson"));
  const decisions = report.findings.map((finding) => ({
    finding_index: finding.index,
    disposition: finding.index === 1 ? "fixed" : "false_positive",
    reason: "Inspected current code.",
    evidence: `Checked ${finding.file}.`,
    validation: "Fixture validation passed.",
  }));
  assert.deepEqual(
    validateRemediation(report, { status: "fixed", summary: "Done", decisions }),
    { fixedCount: 1, hasFix: true },
  );
  assert.throws(
    () => validateRemediation(report, { status: "fixed", summary: "Incomplete", decisions: [] }),
    /one decision per finding/u,
  );
});

test("workflow and remediation prompt use the same structured report path", () => {
  const workflow = fs.readFileSync(
    path.join(repositoryRoot, ".github", "workflows", "automated-review.yml"),
    "utf8",
  );
  const prompt = fs.readFileSync(
    path.join(repositoryRoot, ".github", "codex", "remediation-prompt.md"),
    "utf8",
  );
  assert.match(workflow, /path: \.codex-automation/u);
  assert.match(prompt, /\.codex-automation\/review-report\.json/u);
  assert.doesNotMatch(prompt, /coderabbit-report\.json/u);
});
