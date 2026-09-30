#!/usr/bin/env node
import { runSelectionPhase, writeFinalSummary } from './lib/api-local-selection.mjs';

const phase = process.argv[2];
if (!['verify', 'apply', 'report'].includes(phase)) {
  process.stderr.write('Usage: node scripts/select-local-api-image.mjs <verify|apply|report>\n');
  process.exit(2);
}

try {
  if (phase === 'report') {
    writeFinalSummary();
  } else {
    await runSelectionPhase(phase);
  }
} catch (error) {
  process.stderr.write(`api-local-selection: ${error.message}\n`);
  process.exitCode = 1;
}
