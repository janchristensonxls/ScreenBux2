# 03-final-validation: Build solution and run tests

Perform the final end-to-end validation of the upgraded solution. Run a clean full-solution build and confirm 0 errors and 0 warnings across all 5 projects. Discover and run any test projects; note that the repository currently has no test project, so if none is found, record that there is no automated test coverage to run rather than treating it as a failure.

Capture any deferred, non-blocking recommendations surfaced during the upgrade (e.g., the pre-existing hub URL/port and CORS mismatches noted in the assessment) so they are visible but not silently bundled into this upgrade.

**Done when**: The solution builds cleanly (0 errors, 0 warnings), all discovered tests pass (or the absence of tests is documented), and any deferred recommendations are recorded.
