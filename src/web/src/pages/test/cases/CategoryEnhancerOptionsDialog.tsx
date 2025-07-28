"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";

import { useEffect, useState } from "react";

import { Button } from "@/components/bakaui";
import CategoryEnhancerOptionsDialogPage from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog";
import BApi from "@/sdk/BApi";
const CategoryEnhancerOptionsDialogPage = () => {
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [categories, setCategories] = useState<any[]>([]);

  const [categoryId, setCategoryId] = useState<number>();

  const init = async () => {
    const categories = (await BApi.category.getAllCategories()).data || [];

    setCategories(categories);
    setCategoryId(categories[0]?.id);
    const enhancers =
      (await BApi.enhancer.getAllEnhancerDescriptors()).data || [];

    // @ts-ignore
    setEnhancers(enhancers);
  };

  useEffect(() => {
    init();
  }, []);

  return (
    <div>
      <div>
        {categories.map((c) => {
          return (
            <Button
              color={categoryId == c.id ? "primary" : "default"}
              size={"sm"}
              onClick={() => {
                setCategoryId(c.id);
              }}
            >
              {c.name}
            </Button>
          );
        })}
      </div>
      <div>
        {enhancers.map((e) => {
          return (
            <Button
              size={"sm"}
              onClick={() => {
                if (categoryId) {
                  CategoryEnhancerOptionsDialogPage.show({
                    enhancer: e,
                    categoryId: categoryId,
                  });
                }
              }}
            >
              {e.name}
            </Button>
          );
        })}
      </div>
    </div>
  );
};

CategoryEnhancerOptionsDialogPage.displayName = "CategoryEnhancerOptionsDialogPage";

export default CategoryEnhancerOptionsDialogPage;
