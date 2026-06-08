import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

// ─────────────────────────────────────────────────────────────────────────────
//  cn() — the shadcn-canonical class-name merger.
//
//  WHY clsx AND twMerge BOTH?
//  --------------------------
//  clsx handles conditional joining: cn('a', cond && 'b') → 'a b' or 'a'.
//  twMerge resolves Tailwind class conflicts intelligently: cn('p-4', 'p-2')
//  collapses to 'p-2' (the later wins, semantically), instead of leaving
//  both in the string where the cascade order would decide unpredictably.
//
//  Used by every component that takes a `className` prop so callers can
//  override defaults without specificity battles.
// ─────────────────────────────────────────────────────────────────────────────
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
