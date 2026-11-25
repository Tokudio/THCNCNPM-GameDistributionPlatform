export function acquiredTimestamp(value: string): string {
  // Older or offset-free timestamps use the same UTC convention as UserLibrary.AcquiredAt.
  return value.includes('T') && !/(?:z|[+-]\d{2}:\d{2})$/i.test(value) ? value + 'Z' : value;
}
