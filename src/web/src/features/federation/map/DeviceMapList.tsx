import type { DeviceGraph } from "./graph";

import { useTranslation } from "react-i18next";

import { nodeLabel, relationshipSentences } from "./describe";

/**
 * The map in words, for screen readers: every device with every relationship spelled out.
 * The drawing's controls are named too, but a picture's layout cannot be read out; this
 * says the same things in reading order.
 */
export default function DeviceMapList({ graph }: { graph: DeviceGraph }) {
  const { t } = useTranslation();
  const others = graph.nodes.filter((node) => !node.ghost);
  const ghosts = graph.nodes.filter((node) => node.ghost);

  return (
    <section
      aria-labelledby="device-map-list-title"
      className="sr-only"
      data-testid="device-map-list"
    >
      <h2 id="device-map-list-title">{t("federation.map.list.title")}</h2>
      <ul>
        <li>{nodeLabel(t, graph.self)}</li>
        {others.map((node) => {
          const sentences = relationshipSentences(t, graph, node);

          return (
            <li key={node.id} data-node={node.id}>
              {nodeLabel(t, node)}
              <ul>
                {sentences.length ? (
                  sentences.map((sentence) => <li key={sentence}>{sentence}</li>)
                ) : (
                  <li>{t("federation.map.list.none")}</li>
                )}
              </ul>
            </li>
          );
        })}
      </ul>
      {ghosts.length > 0 && (
        <>
          <h3>{t("federation.map.nearby")}</h3>
          <ul>
            {ghosts.map((node) => (
              <li key={node.id} data-node={node.id}>
                {nodeLabel(t, node)}
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
