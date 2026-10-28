import test from 'node:test';
import assert from 'node:assert/strict';
import {normalizeTitle} from './title.mjs';
test('title whitespace normalization',()=>{
 assert.equal(normalizeTitle('  燕子  UI\t  组件\n 测试 '),'燕子 UI 组件 测试');
 assert.equal(normalizeTitle('\t  '),'');
 assert.equal(normalizeTitle(' A   B   C '),'A B C');
 assert.equal(normalizeTitle(null),'');
 assert.equal(normalizeTitle(32),'');
});
