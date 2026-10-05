import type { DownloadResource, ExtractionPlan } from "./results";

export interface DownloadResourceGroup {
  resource: DownloadResource;
  /** Indices in the persisted result, used by acquisition and local-processing bindings. */
  sourceIndices: number[];
}

const present = (value?: string | null) => (value?.trim() ? value : undefined);
const checkedTime = (value?: string) => {
  const time = Date.parse(value ?? "");

  return Number.isNaN(time) ? -Infinity : time;
};
const equal = (left: unknown, right: unknown): boolean => {
  if (left == null && right == null) return true;
  if (left === right) return true;
  if (!left || !right || typeof left !== "object" || typeof right !== "object") return false;
  if (Array.isArray(left) || Array.isArray(right))
    return (
      Array.isArray(left) &&
      Array.isArray(right) &&
      left.length === right.length &&
      left.every((value, index) => equal(value, right[index]))
    );
  const a = left as Record<string, unknown>;
  const b = right as Record<string, unknown>;

  return [...new Set([...Object.keys(a), ...Object.keys(b)])].every((key) => equal(a[key], b[key]));
};

const extraFieldsAgree = (left: object, right: object, known: string[]) => {
  const a = left as Record<string, unknown>;
  const b = right as Record<string, unknown>;

  return Object.keys(a).every(
    (key) => known.includes(key) || a[key] == null || b[key] == null || equal(a[key], b[key]),
  );
};

const fillMissing = <T extends object>(first: T, second: T): T => {
  const merged = { ...first };

  for (const key of Object.keys(second) as (keyof T)[]) {
    if (merged[key] == null) merged[key] = second[key];
  }

  return merged;
};

/** Preserve path/query/fragment bytes: a different MEGA key or signed query is a different link. */
function identity(resource: DownloadResource) {
  const link = resource.link?.trim();
  let code = present(resource.code);

  if (!link) return undefined;
  const match = /^(https?):\/\/([^/?#]+)(.*)$/i.exec(link);

  if (!match) return { key: link, code, conflict: false };
  try {
    const url = new URL(link);

    if (url.username || url.password) return { key: link, code, conflict: false };
    let suffix = match[3];
    let conflict = false;

    if (["pan.baidu.com", "yun.baidu.com"].includes(url.hostname.toLowerCase())) {
      const fragmentAt = suffix.indexOf("#");
      const fragment = fragmentAt < 0 ? "" : suffix.slice(fragmentAt);
      const pathAndQuery = fragmentAt < 0 ? suffix : suffix.slice(0, fragmentAt);
      const queryAt = pathAndQuery.indexOf("?");

      if (queryAt >= 0) {
        const parts = pathAndQuery.slice(queryAt + 1).split("&");
        const passwords = parts.filter((part) => part.startsWith("pwd="));

        if (passwords.length === 1) {
          const embedded = decodeURIComponent(passwords[0].slice(4).replace(/\+/g, " "));

          if (present(embedded)) {
            conflict = !!code && code !== embedded;
            code ??= embedded;
            const remaining = parts.filter((part) => part !== passwords[0]);

            suffix =
              pathAndQuery.slice(0, queryAt) +
              (remaining.length ? `?${remaining.join("&")}` : "") +
              fragment;
          }
        }
      }
    }

    return { key: `${url.protocol}//${url.host.toLowerCase()}${suffix}`, code, conflict };
  } catch {
    return { key: link, code, conflict: false };
  }
}

const knownPlanFields = ["requirement", "steps", "evidence"];

function mergePlans(
  left?: ExtractionPlan | null,
  right?: ExtractionPlan | null,
): ExtractionPlan | null | undefined | false {
  if (!left || !right) return left ?? right;
  // Keep malformed or newer instruction formats intact rather than flattening them away.
  if (
    !Array.isArray(left.steps) ||
    !Array.isArray(right.steps) ||
    !Array.isArray(left.evidence) ||
    !Array.isArray(right.evidence)
  )
    return false;
  const leftEmpty = left.requirement === "unknown" && left.steps.length === 0;
  const rightEmpty = right.requirement === "unknown" && right.steps.length === 0;

  if (
    !extraFieldsAgree(left, right, knownPlanFields) ||
    (!leftEmpty &&
      !rightEmpty &&
      (left.requirement !== right.requirement || !equal(left.steps, right.steps)))
  )
    return false;
  const evidence = [...new Set([...left.evidence, ...right.evidence])];

  if (evidence.length > 32) return false;

  return { ...fillMissing(leftEmpty ? right : left, leftEmpty ? left : right), evidence };
}

function mergeResources(
  left: DownloadResource,
  right: DownloadResource,
): DownloadResource | undefined {
  const a = identity(left);
  const b = identity(right);

  if (
    !a ||
    !b ||
    a.key !== b.key ||
    a.conflict ||
    b.conflict ||
    (a.code && b.code && a.code !== b.code) ||
    (present(left.password) &&
      present(right.password) &&
      present(left.password) !== present(right.password)) ||
    !extraFieldsAgree(left, right, ["link", "code", "password", "extraction", "linkHealth"])
  )
    return undefined;
  const plan = mergePlans(left.extraction, right.extraction);

  if (plan === false) return undefined;
  const firstHealth = left.linkHealth;
  const secondHealth = right.linkHealth;

  if (
    firstHealth &&
    secondHealth &&
    (!equal(firstHealth.status, secondHealth.status) ||
      !equal(firstHealth.reason, secondHealth.reason) ||
      !extraFieldsAgree(firstHealth, secondHealth, ["checkedAt"]))
  )
    return undefined;
  const health =
    firstHealth && secondHealth
      ? checkedTime(secondHealth.checkedAt) > checkedTime(firstHealth.checkedAt)
        ? fillMissing(secondHealth, firstHealth)
        : fillMissing(firstHealth, secondHealth)
      : (firstHealth ?? secondHealth);

  return {
    ...fillMissing(left, right),
    code: present(left.code) ?? present(right.code) ?? a.code ?? b.code,
    password: present(left.password) ?? present(right.password),
    extraction: plan,
    linkHealth: health,
  };
}

/** Group compatible duplicates without changing persisted resource indices or mutating their contents. */
export function groupDownloadResources(resources: DownloadResource[]): DownloadResourceGroup[] {
  const groups: DownloadResourceGroup[] = [];
  const byLink = new Map<string, DownloadResourceGroup[]>();

  resources.forEach((resource, index) => {
    const key = identity(resource)?.key;

    for (const candidate of key ? (byLink.get(key) ?? []) : []) {
      const merged = mergeResources(candidate.resource, resource);

      if (merged) {
        candidate.resource = merged;
        candidate.sourceIndices.push(index);

        return;
      }
    }
    const group = { resource, sourceIndices: [index] };

    groups.push(group);
    if (key) byLink.set(key, [...(byLink.get(key) ?? []), group]);
  });

  return groups;
}
