const fileLocks = new Map();

export async function withFileLock(key, fn) {
  const previous = fileLocks.get(key) || Promise.resolve();
  let release;
  const current = new Promise(resolve => {
    release = resolve;
  });
  const queued = previous.catch(() => {}).then(() => current);
  fileLocks.set(key, queued);

  await previous.catch(() => {});

  try {
    return await fn();
  } finally {
    release();
    if (fileLocks.get(key) === queued) {
      fileLocks.delete(key);
    }
  }
}

export function fileLockCount() {
  return fileLocks.size;
}
