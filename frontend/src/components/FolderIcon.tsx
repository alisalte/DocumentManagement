/** Stylized folder icon matching the Fillo mockup. */
export function FolderIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 80 64" fill="none" aria-hidden className={className}>
      <path
        d="M8 18c0-3.3 2.7-6 6-6h14l6 8h32c3.3 0 6 2.7 6 6v26c0 3.3-2.7 6-6 6H14c-3.3 0-6-2.7-6-6V18z"
        fill="url(#folder-front)"
      />
      <path
        d="M8 14c0-3.3 2.7-6 6-6h18l4 6H54c3.3 0 6 2.7 6 6v4H8v-10z"
        fill="url(#folder-tab)"
      />
      <defs>
        <linearGradient id="folder-tab" x1="8" y1="8" x2="60" y2="20" gradientUnits="userSpaceOnUse">
          <stop stopColor="#fdba74" />
          <stop offset="1" stopColor="#f97316" />
        </linearGradient>
        <linearGradient id="folder-front" x1="8" y1="18" x2="66" y2="58" gradientUnits="userSpaceOnUse">
          <stop stopColor="#fb923c" />
          <stop offset="1" stopColor="#ea580c" />
        </linearGradient>
      </defs>
    </svg>
  );
}
