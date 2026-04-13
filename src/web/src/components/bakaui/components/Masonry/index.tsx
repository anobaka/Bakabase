import React from "react";

type Props = {
  columns?: number;
  columnGap?: string;
  rowGap?: string;
  children: React.ReactNode;
  className?: string;
};

/**
 * Masonry layout that distributes children across columns in row-first order.
 * Items flow left-to-right then top-to-bottom, while each column allows
 * variable item heights (no fixed row height).
 */
const Masonry: React.FC<Props> = ({
  columns = 2,
  columnGap = "1rem",
  rowGap = "0.5rem",
  children,
  className,
}) => {
  const items = React.Children.toArray(children).filter(Boolean);

  // Distribute items round-robin across columns for row-first ordering
  const columnItems: React.ReactNode[][] = Array.from({ length: columns }, () => []);
  items.forEach((item, i) => {
    columnItems[i % columns].push(item);
  });

  return (
    <div className={className} style={{ display: "flex", gap: columnGap, alignItems: "flex-start" }}>
      {columnItems.map((colItems, colIndex) => (
        <div
          key={colIndex}
          style={{
            flex: 1,
            minWidth: 0,
            display: "flex",
            flexDirection: "column",
            gap: rowGap,
          }}
        >
          {colItems}
        </div>
      ))}
    </div>
  );
};

Masonry.displayName = "Masonry";

export default Masonry;
