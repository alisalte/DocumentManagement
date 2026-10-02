import { describe, expect, it } from 'vitest';
import type { CategoryNode } from './api';
import { topLevelArchiveFolders } from './categories';

function folder(partial: Partial<CategoryNode> & Pick<CategoryNode, 'id' | 'name'>): CategoryNode {
  return {
    parentId: null,
    code: partial.id,
    description: null,
    depth: 0,
    isActive: true,
    sortOrder: 0,
    canView: true,
    canCreate: true,
    ...partial,
  };
}

describe('topLevelArchiveFolders', () => {
  it('lists children of the single archive root', () => {
    const categories = [
      folder({ id: 'root', name: 'اسناد', code: 'ROOT', sortOrder: 0 }),
      folder({ id: 'a', name: 'قراردادها', parentId: 'root', sortOrder: 2 }),
      folder({ id: 'b', name: 'مالی', parentId: 'root', sortOrder: 1 }),
      folder({ id: 'nested', name: 'قدیمی', parentId: 'a', sortOrder: 1 }),
    ];

    expect(topLevelArchiveFolders(categories).map((item) => item.id)).toEqual(['b', 'a']);
  });

  it('skips children the user cannot view', () => {
    const categories = [
      folder({ id: 'root', name: 'اسناد' }),
      folder({ id: 'secret', name: 'محرمانه', parentId: 'root', canView: false }),
      folder({ id: 'open', name: 'عمومی', parentId: 'root' }),
    ];

    expect(topLevelArchiveFolders(categories).map((item) => item.id)).toEqual(['open']);
  });

  it('falls back to the root when it has no viewable children', () => {
    const categories = [folder({ id: 'root', name: 'اسناد' })];
    expect(topLevelArchiveFolders(categories).map((item) => item.id)).toEqual(['root']);
  });

  it('lists null-parent folders when there is no single archive root', () => {
    const categories = [
      folder({ id: 'd2', name: 'دوم', sortOrder: 2 }),
      folder({ id: 'd1', name: 'اول', sortOrder: 1 }),
      folder({ id: 'child', name: 'زیر', parentId: 'd1' }),
    ];

    expect(topLevelArchiveFolders(categories).map((item) => item.id)).toEqual(['d1', 'd2']);
  });
});
