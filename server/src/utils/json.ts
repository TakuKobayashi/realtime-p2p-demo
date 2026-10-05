// T describes the expected shape; callers must validate received data at runtime.
export function parseJson<T = unknown>(raw: string): T | undefined {
  try {
    return JSON.parse(raw) as T;
  } catch {
    return undefined;
  }
}
