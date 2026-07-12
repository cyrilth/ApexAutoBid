interface ArrowIconProps {
  direction: "up" | "down";
  className?: string;
}

/**
 * Keyboard/tap-accessible reorder glyph for `components/AuctionImageManager.tsx`
 * -- a fallback for drag-to-reorder (Task 6.3), which pointer-only drag
 * handles alone can't offer (Docs/DesignGuide.md §9 accessibility).
 */
export function ArrowIcon({ direction, className }: ArrowIconProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
    >
      {direction === "up" ? <path d="M18 15l-6-6-6 6" /> : <path d="M6 9l6 6 6-6" />}
    </svg>
  );
}
