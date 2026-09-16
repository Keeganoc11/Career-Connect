import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Self-hosted, so there's no external font request — nothing to allow in production.
import '@fontsource-variable/inter'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
