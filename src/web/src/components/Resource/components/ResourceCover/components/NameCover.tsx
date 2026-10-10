import type { CSSProperties } from "react";

import "./NameCover.scss";

const palettes = [
  ["#203b35", "#b9dbab"],
  ["#263747", "#a9cce3"],
  ["#50322f", "#ebbc91"],
  ["#432c3b", "#e0b1bf"],
  ["#3e3b28", "#e2d9a4"],
  ["#303d52", "#abbbe9"],
] as const;

export function nameCoverAppearance(name: string) {
  const normalized = name.normalize("NFKC").trim();
  let hash = 2166136261;
  for (const character of normalized) {
    hash = Math.imul(hash ^ character.codePointAt(0)!, 16777619) >>> 0;
  }
  const words = normalized.match(/[\p{L}]+/gu) ?? [];
  const initials =
    words.length > 1
      ? `${Array.from(words[0])[0]}${Array.from(words[1])[0]}`
      : Array.from(words[0] ?? normalized)
          .slice(0, 2)
          .join("");

  return { colors: palettes[hash % palettes.length], initials: initials.toUpperCase() };
}

/** A lightweight, deterministic typographic cover; no generated image or remote request. */
export default function NameCover({ name }: { name: string }) {
  const { colors, initials } = nameCoverAppearance(name);
  const style = { "--name-cover-bg": colors[0], "--name-cover-accent": colors[1] } as CSSProperties;

  return (
    <div aria-label={name} className="resource-name-cover" role="img" style={style} title={name}>
      <div aria-hidden className="resource-name-cover__art">
        <div className="resource-name-cover__orbit" />
        <span className="resource-name-cover__initials">{initials}</span>
      </div>
      <div className="resource-name-cover__caption">
        <span aria-hidden className="resource-name-cover__rule" />
        <span
          className={`resource-name-cover__title${name.length > 60 ? " resource-name-cover__title--long" : ""}`}
        >
          {name.split(/([._])/).map((part, index) =>
            part === "." || part === "_" ? (
              <span key={index}>
                {part}
                <wbr />
              </span>
            ) : (
              part
            ),
          )}
        </span>
      </div>
    </div>
  );
}
