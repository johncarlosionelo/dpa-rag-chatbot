import { twMerge } from 'tailwind-merge';

export function cn(...parts: unknown[]): string {
  return twMerge(parts.filter((part): part is string => typeof part === 'string').join(' '));
}
