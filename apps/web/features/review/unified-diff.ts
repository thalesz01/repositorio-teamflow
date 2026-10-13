export type DiffLine = { kind: "added" | "removed" | "context" | "hunk" | "meta"; text: string; before?: number; after?: number };
export type DiffFile = { path: string; status: "added" | "removed" | "modified" | "renamed"; binary: boolean; additions: number; deletions: number; lines: DiffLine[] };

function gitPath(value: string, prefixed = true) {
  if (value.startsWith('"')) {
    const bytes: number[] = [];
    const encoder = new TextEncoder();
    for (let i = 1; i < value.length && value[i] !== '"'; i++) {
      if (value[i] !== "\\") { bytes.push(...encoder.encode(value[i])); continue; }
      const octal = value.slice(i + 1).match(/^[0-7]{3}/)?.[0];
      if (octal) { bytes.push(parseInt(octal, 8)); i += 3; }
      else { i++; bytes.push(...encoder.encode(({ t: "\t", n: "\n", r: "\r" } as Record<string, string>)[value[i]] ?? value[i])); }
    }
    value = new TextDecoder().decode(new Uint8Array(bytes));
  }
  return prefixed ? value.replace(/^[ab]\//, "") : value;
}

export function parseUnifiedDiff(text: string): DiffFile[] {
  const files: DiffFile[] = [];
  let file: DiffFile | undefined;
  let before = 0, after = 0, inHunk = false;
  for (const line of text.replace(/\r\n/g, "\n").split("\n")) {
    if (line.startsWith("diff --git ")) {
      file = { path: gitPath(line.match(/ ("b\/.+"|b\/.+)$/)?.[1] ?? line.slice(11)), status: "modified", binary: false, additions: 0, deletions: 0, lines: [] };
      files.push(file); inHunk = false; continue;
    }
    if (!file) continue;
    const hunk = line.match(/^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/);
    if (hunk) { before = Number(hunk[1]); after = Number(hunk[2]); inHunk = true; file.lines.push({ kind: "hunk", text: line }); continue; }
    if (inHunk) {
      if (line.startsWith("+")) { file.additions++; file.lines.push({ kind: "added", text: line.slice(1), after: after++ }); }
      else if (line.startsWith("-")) { file.deletions++; file.lines.push({ kind: "removed", text: line.slice(1), before: before++ }); }
      else if (line.startsWith(" ")) file.lines.push({ kind: "context", text: line.slice(1), before: before++, after: after++ });
      else if (line.startsWith("\\")) file.lines.push({ kind: "meta", text: "Sem quebra de linha no fim do arquivo" });
      continue;
    }
    if (line.startsWith("+++ ") && line.slice(4) !== "/dev/null") file.path = gitPath(line.slice(4));
    if (line.startsWith("--- ") && line.slice(4) !== "/dev/null" && file.status === "removed") file.path = gitPath(line.slice(4));
    if (line.startsWith("new file mode")) file.status = "added";
    if (line.startsWith("deleted file mode")) file.status = "removed";
    if (line.startsWith("rename to ")) { file.status = "renamed"; file.path = gitPath(line.slice(10), false); }
    if (line.startsWith("Binary files ") || line === "GIT binary patch") file.binary = true;
  }
  return files;
}
