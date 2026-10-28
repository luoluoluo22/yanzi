// Only READS GitHub. It never merges or approves a pull request.
// Use as createBridge({ integrationVerifier: input => verifyMergedPR(input, config) })
// only with an explicitly configured repository and nonempty required CI checks.
export async function verifyMergedPR(input, {
  repository, prNumber, baseBranch = 'main', requiredChecks,
  token = '', fetchImpl = fetch,
} = {}) {
  if (!/^[a-zA-Z0-9_.-]+\/[a-zA-Z0-9_.-]+$/.test(repository || '') ||
      !Number.isSafeInteger(prNumber) || prNumber < 1 ||
      !/^[a-zA-Z0-9_./-]{1,80}$/.test(baseBranch) ||
      !Array.isArray(requiredChecks) || requiredChecks.length < 1 ||
      requiredChecks.some(x => typeof x !== 'string' || !x || x.length > 128))
    throw new Error('github_verifier_policy_invalid');
  const sha = /^[0-9a-f]{40}$/i;
  if (!sha.test(input.reviewedCommitSha) || !sha.test(input.mergeCommitSha))
    throw new Error('github_verifier_sha_invalid');
  const commonHeaders = { Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28' };
  if (token) commonHeaders.Authorization = 'Bearer ' + token;
  const base = 'https://api.github.com/repos/' + repository;
  async function read(url) {
    const response = await fetchImpl(url, { headers: commonHeaders,
      signal: AbortSignal.timeout(15000) });
    if (!response.ok) throw new Error('github_read_failed_' + response.status);
    return response.json();
  }
  const pr = await read(base + '/pulls/' + prNumber);
  if (pr.merged !== true || pr.state !== 'closed' ||
      pr.base?.ref !== baseBranch || pr.head?.sha !== input.reviewedCommitSha ||
      pr.merge_commit_sha !== input.mergeCommitSha)
    throw new Error('github_pr_merge_identity_mismatch');

  const [checks, status] = await Promise.all([
    read(base + '/commits/' + input.reviewedCommitSha + '/check-runs?per_page=100'),
    read(base + '/commits/' + input.reviewedCommitSha + '/status'),
  ]);
  // A neutral/skipped conclusion is not a passing required check.
  const runSuccess = new Set((checks.check_runs || [])
    .filter(x => x.status === 'completed' && x.conclusion === 'success')
    .map(x => x.name));
  const statusSuccess = new Set((status.statuses || [])
    .filter(x => x.state === 'success').map(x => x.context));
  for (const name of requiredChecks) {
    if (!runSuccess.has(name) && !statusSuccess.has(name))
      throw new Error('required_ci_not_successful:' + name.slice(0, 70));
  }
  return { ciPassed: true,
    proof: 'GitHub PR#' + prNumber + ' merged; candidate=' +
      input.reviewedCommitSha.slice(0, 12) + '; merge=' +
      input.mergeCommitSha.slice(0, 12) + '; required checks: ' + requiredChecks.join(', ') };
}
