// Resumo da cobertura como o Sonar mede (unitários, integrados e funcionais): junta os coverage.opencover.xml de TestResults/coverage, aplica as exclusões
// de cobertura da esteira (static-analysis.yml) e mostra o total, o total do que mudou em relação à main (o "new code"
// do PR) e os arquivos com menos cobertura.
//
// Uso: node .claude/skills/local-tests/scripts/coverage-summary.mjs [raiz do repositório]
import { execSync } from "node:child_process";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";

const root = process.argv[2] ?? process.cwd();

// Mesmo valor de SONAR_COVERAGE_EXCLUSIONS no static-analysis.yml.
const workflow = readFileSync(join(root, ".github/workflows/static-analysis.yml"), "utf8");
const exclusions = (workflow.match(/SONAR_COVERAGE_EXCLUSIONS:\s*"([^"]+)"/)?.[1] ?? "").split(",").filter(Boolean);
const globToRegex = (glob) =>
  new RegExp(`^${glob.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*\*\//g, "(.*/)?").replace(/\*\*/g, ".*").replace(/\*/g, "[^/]*")}$`);
const excluded = exclusions.map(globToRegex);

function findReports(directory) {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) return findReports(path);
    return entry === "coverage.opencover.xml" ? [path] : [];
  });
}

// Linha ou condição coberta em qualquer relatório conta como coberta, como no Sonar. O Sonar soma as duas:
// (linhas cobertas + condições cobertas) / (linhas + condições).
const lines = new Map();
const branches = new Map();
const mark = (map, file, key, hit) => {
  const fileMap = map.get(file) ?? new Map();
  fileMap.set(key, (fileMap.get(key) ?? false) || hit);
  map.set(file, fileMap);
};
for (const report of findReports(join(root, "TestResults/coverage"))) {
  const xml = readFileSync(report, "utf8");
  const files = new Map([...xml.matchAll(/<File uid="(\d+)" fullPath="([^"]+)"/g)].map(([, uid, path]) => [uid, path]));
  for (const [, body] of xml.matchAll(/<Method [\s\S]*?>([\s\S]*?)<\/Method>/g)) {
    const uid = body.match(/<FileRef uid="(\d+)" \/>/)?.[1];
    if (!uid) continue;
    const file = relative(root, files.get(uid)).split(sep).join("/");
    if (!file.startsWith("src/") || excluded.some((regex) => regex.test(file))) continue;
    lines.set(file, lines.get(file) ?? new Map());
    for (const [, vc, line] of body.matchAll(/<SequencePoint vc="(\d+)"[^>]*? sl="(\d+)"/g)) mark(lines, file, line, Number(vc) > 0);
    for (const [point] of body.matchAll(/<BranchPoint [^>]*>/g)) {
      const attribute = (name) => point.match(new RegExp(` ${name}="(\\d+)"`))?.[1];
      mark(branches, file, `${attribute("sl")}:${attribute("offset")}:${attribute("path")}`, Number(attribute("vc")) > 0);
    }
  }
}

const changed = new Set(
  execSync("git diff --name-only origin/main...HEAD", { cwd: root, encoding: "utf8" })
    .concat(execSync("git diff --name-only", { cwd: root, encoding: "utf8" }))
    .split("\n")
    .filter(Boolean),
);

function summarize(files) {
  let covered = 0;
  let total = 0;
  for (const file of files) {
    for (const hit of [...lines.get(file).values(), ...(branches.get(file)?.values() ?? [])]) {
      total++;
      if (hit) covered++;
    }
  }
  return { covered, total, percentage: total ? ((covered / total) * 100).toFixed(1) : "100.0" };
}

// Linhas com algo descoberto: a própria linha ou uma condição dela.
function uncoveredLines(file) {
  const result = new Set([...lines.get(file)].filter(([, hit]) => !hit).map(([line]) => Number(line)));
  for (const [key, hit] of branches.get(file) ?? []) if (!hit) result.add(Number(key.split(":")[0]));
  return [...result].sort((a, b) => a - b);
}

const all = [...lines.keys()];
const ofChange = all.filter((file) => changed.has(file));
const overall = summarize(all);
const change = summarize(ofChange);

console.log(`\n[ntix] Cobertura (unitários + integrados + funcionais, exclusões do Sonar; linhas + condições)`);
console.log(`  Total:              ${overall.percentage}% (${overall.covered}/${overall.total})`);
console.log(`  Arquivos alterados: ${change.percentage}% (${change.covered}/${change.total})\n`);

const rows = (ofChange.length ? ofChange : all)
  .map((file) => ({ file, ...summarize([file]) }))
  .filter((row) => row.covered < row.total)
  .sort((a, b) => a.covered / a.total - b.covered / b.total);

for (const row of rows)
  console.log(`  ${row.percentage.padStart(5)}%  ${String(row.total - row.covered).padStart(3)} itens  ${row.file}  [linhas ${uncoveredLines(row.file).join(", ")}]`);
