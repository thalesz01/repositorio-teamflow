import type { NextConfig } from "next";

const apiUrl = process.env.API_URL ?? "http://localhost:5080";
const url = new URL(apiUrl);
if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.search || url.hash || url.pathname !== "/") {
  throw new Error("API_URL must be an HTTP(S) origin without credentials.");
}

const config: NextConfig = {
  // A separate build directory lets a dev server run next to a production preview without overwriting it.
  distDir: process.env.NEXT_DIST_DIR ?? ".next",
  // `next dev` would otherwise write an AGENTS.md into the app; agent rules belong to the repository root.
  agentRules: false,
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${url.origin}/api/:path*` }];
  },
};

export default config;
