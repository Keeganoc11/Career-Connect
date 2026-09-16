import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Self-hosted, so there's no external font request — nothing to allow in production.
import '@fontsource-variable/inter'
import './index.css'
import App from './App.tsx'
import { Toaster } from './components/ui/Toaster.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

// Its own root, outside #root: the overlay stack marks #root inert while a
// dialog is open, which would otherwise silence a toast that dialog just raised.
createRoot(document.getElementById('toaster')!).render(
  <StrictMode>
    <Toaster />
  </StrictMode>,
)
