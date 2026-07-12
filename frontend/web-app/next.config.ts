import type { NextConfig } from "next";
import withFlowbiteReact from "flowbite-react/plugin/nextjs";
import { IMAGE_STORAGE_URL } from "./lib/image-config";

const nextConfig: NextConfig = {
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
  },
};

export default withFlowbiteReact(nextConfig);