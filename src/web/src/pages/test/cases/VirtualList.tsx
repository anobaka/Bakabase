"use client";

import React, { useState, useRef } from "react";
import { List } from "react-virtualized";
const VirtualListPage = () => {
  const [items, setItems] = useState([1, 2, 3, 4, 5]);
  const listRef = useRef();

  const removeItem = (index) => {
    setItems((prevItems) => prevItems.filter((_, i) => i !== index));
    // listRef.current?.forceUpdateGrid();
  };

  return (
    <List
      ref={listRef}
      height={300}
      rowCount={items.length}
      rowHeight={20}
      rowRenderer={({ index, key, style }) => (
        <div key={key} style={style}>
          {items[index]}
          <button onClick={() => removeItem(index)}>Remove</button>
        </div>
      )}
      width={300}
    />
  );
};

VirtualListPage.displayName = "VirtualListPage";

export default VirtualListPage;
