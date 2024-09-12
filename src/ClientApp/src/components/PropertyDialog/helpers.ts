export const optimizeOptions = (options?: any) => {
  if (!options) {
    return {};
  }
  if (options.choices) {
    options.choices = options.choices.map((c: any) => {
      return {
        ...c,
        label: c.label?.trim(),
      };
    });
  }

  if (options.tags) {
    options.tags = options.tags.map((t: any) => {
      return {
        ...t,
        name: t.name?.trim(),
        group: t.group?.trim(),
      };
    });
  }

  return {
    ...options,
    choices: options.choices || [],
    tags: options.tags || [],
  };
};
