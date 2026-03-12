import { defineConfig } from '@hey-api/openapi-ts';

export default defineConfig({
  client: '@hey-api/client-axios',
  input: '../../analyse_technique/openapi.yaml',
  output: {
    path: './src/lib/api/generated',
    format: 'prettier',
    lint: 'eslint',
  },
});
