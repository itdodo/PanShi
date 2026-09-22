import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { permissionDirective } from './directives/permission'
import './styles/global.css'

const app = createApp(App)

// pinia 必须先于 router 安装：守卫里要用 store
app.use(createPinia())
app.use(router)
app.directive('permission', permissionDirective)

app.mount('#app')
