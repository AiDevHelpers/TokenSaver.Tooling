import js from "@eslint/js";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist/**", "bin/**", "obj/**", "node_modules/**"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
);
