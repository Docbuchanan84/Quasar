import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../index.css'
import './torch.css'
import TorchDesigner from './TorchDesigner'

createRoot(document.getElementById('root')!).render(
  <StrictMode><TorchDesigner /></StrictMode>,
)
