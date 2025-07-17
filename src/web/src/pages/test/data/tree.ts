type TreeNode = {id: number; value: string; children?: TreeNode[]};

const generateTrees = (): TreeNode[] => {
  const trees: TreeNode[] = [];
  const treeCount = Math.floor(Math.random() * 10) + 5;
  for (let i = 0; i < treeCount; i++) {
    const depth = Math.floor(Math.random() * 5);
    const branchCount = Math.floor(Math.random() * 5);
    console.log(depth, branchCount);
    trees.push({
      id: i,
      value: Math.random().toString(36).substring(7),
      children: generateTreeLayer(depth, branchCount),
    });
  }
  return trees;
};

const generateTreeLayer = (restDepth: number, maxBranchCount: number): TreeNode[] => {
  const nodes: TreeNode[] = [];
  const branchCount = Math.floor(Math.random() * maxBranchCount);
  for (let i = 0; i < branchCount; i++) {
    const node: TreeNode = {
      id: Math.floor(Math.random() * 100),
      value: Math.random().toString(36).substring(7),
    };
    if (restDepth > 0) {
      node.children = generateTreeLayer(restDepth - 1, maxBranchCount);
    }
    nodes.push(node);
  }
  return nodes;
};

export {
  generateTrees,
};
