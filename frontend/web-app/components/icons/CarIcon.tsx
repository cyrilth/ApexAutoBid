interface CarIconProps {
  className?: string;
}

/** Shared placeholder glyph for `CarImage` and `EmptyState` (Docs/DesignGuide.md §5/§6). */
export function CarIcon({ className }: CarIconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
    >
      <path d="M3 13l1.5-4.5A2 2 0 0 1 6.4 7h11.2a2 2 0 0 1 1.9 1.5L21 13" />
      <path d="M3 13h18v4a1 1 0 0 1-1 1h-1a1 1 0 0 1-1-1v-1H6v1a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-4Z" />
      <circle cx="7.5" cy="16.5" r="0.5" fill="currentColor" />
      <circle cx="16.5" cy="16.5" r="0.5" fill="currentColor" />
    </svg>
  );
}
