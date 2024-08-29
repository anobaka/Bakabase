import React, { useState, useRef } from 'react';
import { List } from 'react-virtualized';

export default () => {
  const [items, setItems] = useState([1, 2, 3, 4, 5]);
  const listRef = useRef();

  const removeItem = (index) => {
    setItems((prevItems) => prevItems.filter((_, i) => i !== index));
    // listRef.current?.forceUpdateGrid();
  };

  return (
    <List
      ref={listRef}
      width={300}
      height={300}
      rowCount={items.length}
      rowHeight={20}
      rowRenderer={({ index, key, style }) => (
        <div key={key} style={style}>
          {items[index]}
          <button onClick={() => removeItem(index)}>Remove</button>
        </div>
      )}
    />
  );
};
