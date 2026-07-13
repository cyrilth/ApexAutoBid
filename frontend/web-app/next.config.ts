import type { NextConfig } from "next";
import withFlowbiteReact from "flowbite-react/plugin/nextjs";
import { IMAGE_STORAGE_URL } from "./lib/image-config";

const nextConfig: NextConfig = {
  // Phase 7 Task 13: emits a self-contained `.next/standalone` build (a minimal
  // `server.js` plus only the `node_modules` files actually traced as needed at
  // runtime) so the Docker runtime stage doesn't need `npm install`/full
  // `node_modules` at all -- see Dockerfile. No effect on `next dev`/`next start`.
  output: "standalone",
  images: {
    // Whitelists the MinIO/object-storage host (dev default, or
    // NEXT_PUBLIC_IMAGE_STORAGE_URL in production -- see
    // lib/image-config.ts and .env.example) that serves auction images
    // and thumbnails. Phase 7 Task 12: `next/image` refuses to optimize
    // any remote host not listed here.
    remotePatterns: [new URL(`${IMAGE_STORAGE_URL}/auction-images/**`)],
    // Required starting Next.js 16 (unrestricted quality values could let a
    // caller force arbitrarily expensive re-encodes) -- 75 is next/image's
    // own default, kept as the only allowed value since nothing in this app
    // passes a custom `quality` prop.
    qualities: [75],
    // Next.js 16 refuses to optimize upstream images whose host resolves to
    // a private/loopback IP (SSRF hardening). Both local-dev shapes of this
    // app hit that wall: dev MinIO lives at localhost:9000, and the Phase 8
    // docker-compose stack serves images at storage.apexautobid.local -- a
    // dev-only hostname that resolves to the Nginx container's private
    // network IP. Allow exactly those two shapes (loopback, or a *.local
    // dev domain, which can never be a real public host -- RFC 6762
    // reserves .local); a real production host never enables this.
    dangerouslyAllowLocalIP:
      ["localhost", "127.0.0.1"].includes(new URL(IMAGE_STORAGE_URL).hostname) ||
      new URL(IMAGE_STORAGE_URL).hostname.endsWith(".local"),
  },
};

export default withFlowbiteReact(nextConfig);