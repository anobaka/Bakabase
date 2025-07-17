export class AliasCommon {
  id: number;
  name: string;
  preferredAlias: string;

  get displayName(): string {
    if (this.preferredAlias?.length > 0) {
      return this.preferredAlias;
    }

    return this.name;
  }

  set displayName(v) {}

  constructor(init?: Partial<AliasCommon>) {
    Object.assign(this, init);
  }
}
