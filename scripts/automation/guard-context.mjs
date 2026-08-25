#!/usr/bin/env node

import fs from "node:fs";
import process from "node:process";
import { pathToFileURL } from "node:url";

export const MAX_REMEDIATION_CYCLES = 5;
const ALLOWED_ACTORS = new Set(["ellermw", "github-actions[bot]"]);
const AUTOMATION_BRANCH = "automation/review-remediation-loop";

export function evaluateContext(input) {
  const reasons = [];
  const cycle = Number(input.cycle);

  if (input.repository !== "ellermw/SiloDesktopApp")
    reasons.push("unexpected repository");
  if (input.headRepository !== input.repository)
    reasons.push("fork pull requests are not eligible");
  if (input.baseRef !== "main")
    reasons.push("base branch must be main");
  if (!input.headRef || input.headRef === "main")
    reasons.push("head branch must be a non-main branch");
  if (!ALLOWED_ACTORS.has(input.actor))
    reasons.push("workflow actor is not trusted");
  if (!Number.isInteger(cycle) || cycle < 0 || cycle > MAX_REMEDIATION_CYCLES)
    reasons.push("cycle is outside the allowed range");
  if (!input.expectedHeadSha)
    reasons.push("expected head SHA is required");
  else if (input.expectedHeadSha !== input.currentHeadSha)
    reasons.push("review request is stale");

  const eligible = reasons.length === 0;
  return {
    eligible,
    canRemediate:
      eligible && cycle < MAX_REMEDIATION_CYCLES && input.headRef !== AUTOMATION_BRANCH,
    reasons,
    cycle,
    maxCycles: MAX_REMEDIATION_CYCLES,
  };
}

function output(name, value) {
  if (!process.env.GITHUB_OUTPUT) return;
  fs.appendFileSync(process.env.GITHUB_OUTPUT, `${name}=${String(value)}\n`);
}

function loadJson(path) {
  return JSON.parse(fs.readFileSync(path, "utf8"));
}

function main() {
  const eventPath = process.argv[2];
  const prPath = process.argv[3];
  if (!eventPath) throw new Error("usage: guard-context.mjs <event.json> [pull-request.json]");

  const event = loadJson(eventPath);
  const eventName = process.env.GITHUB_EVENT_NAME;
  const pr = eventName === "pull_request" ? event.pull_request : loadJson(prPath);
  if (!pr?.number || !pr?.head?.sha || !pr?.base?.sha)
    throw new Error("pull request context is incomplete");

  const cycle = eventName === "workflow_dispatch" ? event.inputs?.cycle ?? "0" : "0";
  const expectedHeadSha =
    eventName === "workflow_dispatch" ? event.inputs?.expected_head_sha ?? "" : pr.head.sha;

  const result = evaluateContext({
    repository: process.env.GITHUB_REPOSITORY,
    headRepository: pr.head.repo?.full_name,
    baseRef: pr.base.ref,
    headRef: pr.head.ref,
    actor: process.env.GITHUB_ACTOR,
    cycle,
    expectedHeadSha,
    currentHeadSha: pr.head.sha,
  });

  output("eligible", result.eligible);
  output("can_remediate", result.canRemediate);
  output("reason", result.reasons.join("; ") || "eligible");
  output("pr_number", pr.number);
  output("head_sha", pr.head.sha);
  output("base_sha", pr.base.sha);
  output("head_ref", pr.head.ref);
  output("cycle", result.cycle);
  output("max_cycles", result.maxCycles);

  process.stdout.write(`${JSON.stringify({
    ...result,
    prNumber: pr.number,
    headSha: pr.head.sha,
    baseSha: pr.base.sha,
    headRef: pr.head.ref,
  })}\n`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
