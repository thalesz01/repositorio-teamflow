import { mkdir, readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import openapiTS, { astToString } from "openapi-typescript";

const input = process.argv[2] ?? "../../contracts/api/openapi.json";
const source = /^https?:\/\//.test(input) ? new URL(input) : pathToFileURL(resolve(input));
const schema = source.protocol === "file:"
  ? JSON.parse(await readFile(source, "utf8"))
  : await (async () => {
    const response = await fetch(source, { signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error(`OpenAPI request failed: ${response.status}`);
    return response.json();
  })();
const content = "/** Generated from OpenAPI. Do not edit manually. */\n" + astToString(await openapiTS(schema));
await mkdir("api", { recursive: true });
await writeFile("api/schema.d.ts", content);
console.log("Generated api/schema.d.ts");
