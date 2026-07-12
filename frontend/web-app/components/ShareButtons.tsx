"use client";

import { useSyncExternalStore } from "react";
import { FacebookIcon, ShareIcon, WhatsAppIcon, XIcon } from "@/components/icons/ShareIcons";
import { toastError } from "@/lib/toast";

interface ShareButtonsProps {
  /** Canonical, absolute auction URL -- server-computed via lib/site-url.ts. */
  url: string;
  title: string;
  text: string;
}

const ICON_BUTTON_CLASS =
  "inline-flex h-9 w-9 items-center justify-center rounded-full border border-slate-300 text-slate-600 transition-colors hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-400";

// `navigator.share` support never changes after mount, so there's nothing to
// subscribe to -- this is purely a `useSyncExternalStore` trick to read a
// browser-only API without a hydration mismatch (the "external system" is
// just "are we actually running in a browser that supports it").
function subscribe() {
  return () => {};
}
function getSnapshot() {
  return typeof navigator !== "undefined" && typeof navigator.share === "function";
}
function getServerSnapshot() {
  return false;
}

/**
 * Social share row (Task 5.5, Docs/Requirements.md §4 -- "share buttons row
 * under the specs"): share-intent links for Facebook/X/WhatsApp, plus a
 * native Web Share API button when the browser supports it (opens the OS
 * share sheet on mobile -- the only way to reach Instagram, which has no web
 * share URL of its own).
 *
 * Client Component: intent links need `target="_blank"` browser navigation
 * and `navigator.share`/`navigator` feature-detection is inherently
 * browser-only.
 */
export function ShareButtons({ url, title, text }: ShareButtonsProps) {
  // `getServerSnapshot` returns false so the server-rendered and first
  // client-rendered markup match -- `getSnapshot` then takes over on the
  // client, revealing the native-share button only when the browser
  // actually supports it, without a hydration mismatch.
  const canNativeShare = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  const facebookHref = `https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(url)}`;
  const twitterHref = `https://twitter.com/intent/tweet?url=${encodeURIComponent(url)}&text=${encodeURIComponent(text)}`;
  const whatsappHref = `https://wa.me/?text=${encodeURIComponent(`${text} ${url}`)}`;

  async function handleNativeShare() {
    try {
      await navigator.share({ title, text, url });
    } catch (error) {
      // AbortError just means the visitor closed the share sheet -- not a real failure.
      if (error instanceof Error && error.name !== "AbortError") {
        toastError("Couldn't open the share sheet.");
      }
    }
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-sm text-slate-500">Share:</span>

      <a
        href={facebookHref}
        target="_blank"
        rel="noopener noreferrer"
        aria-label="Share on Facebook"
        className={ICON_BUTTON_CLASS}
      >
        <FacebookIcon className="h-4 w-4" />
      </a>

      <a
        href={twitterHref}
        target="_blank"
        rel="noopener noreferrer"
        aria-label="Share on X"
        className={ICON_BUTTON_CLASS}
      >
        <XIcon className="h-4 w-4" />
      </a>

      <a
        href={whatsappHref}
        target="_blank"
        rel="noopener noreferrer"
        aria-label="Share on WhatsApp"
        className={ICON_BUTTON_CLASS}
      >
        <WhatsAppIcon className="h-4 w-4" />
      </a>

      {canNativeShare && (
        <button type="button" onClick={handleNativeShare} aria-label="Share" className={ICON_BUTTON_CLASS}>
          <ShareIcon className="h-4 w-4" />
        </button>
      )}
    </div>
  );
}
