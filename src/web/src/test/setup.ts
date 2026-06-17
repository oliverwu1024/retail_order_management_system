// Vitest global setup. The `/vitest` entrypoint registers jest-dom's matchers
// (toBeInTheDocument, toHaveTextContent, …) AND augments Vitest's `expect`
// types, so the matchers type-check in test files. RTL's cleanup runs after
// each test to unmount the previous render (jsdom is shared across a file).
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => {
  cleanup()
})
