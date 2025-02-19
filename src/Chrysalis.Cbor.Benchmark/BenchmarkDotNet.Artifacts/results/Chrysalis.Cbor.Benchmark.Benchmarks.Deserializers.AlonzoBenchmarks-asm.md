## .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2
```assembly
; Chrysalis.Cbor.Benchmark.Benchmarks.Deserializers.AlonzoBenchmarks.New()
       push      rbp
       sub       rsp,20
       lea       rbp,[rsp+20]
       vxorps    xmm8,xmm8,xmm8
       vmovdqu   ymmword ptr [rbp-20],ymm8
       mov       [rbp-20],rdi
       mov       dword ptr [rbp-18],0FFFFFFFF
       lea       rdi,[rbp-20]
       call      qword ptr [7F8F41704C18]
       mov       rax,[rbp-10]
       test      rax,rax
       je        short M00_L01
M00_L00:
       add       rsp,20
       pop       rbp
       ret
M00_L01:
       lea       rdi,[rbp-10]
       call      qword ptr [7F8F418CE3B8]
       jmp       short M00_L00
; Total bytes of code 68
```
```assembly
; BenchmarkDotNet.Helpers.AwaitHelper.GetResult(System.Threading.Tasks.Task)
       push      rax
       mov       esi,[rdi+34]
       and       esi,11000000
       cmp       esi,1000000
       jne       short M01_L01
M01_L00:
       add       rsp,8
       ret
M01_L01:
       xor       esi,esi
       call      qword ptr [7F8F41706D78]; System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task, System.Threading.Tasks.ConfigureAwaitOptions)
       jmp       short M01_L00
; Total bytes of code 33
```
```assembly
; System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task, System.Threading.Tasks.ConfigureAwaitOptions)
       push      rbp
       push      r15
       push      rbx
       lea       rbp,[rsp+10]
       mov       rbx,rdi
       mov       r15d,esi
       test      dword ptr [rbx+34],1600000
       jne       short M02_L00
       mov       rdi,rbx
       xor       edx,edx
       mov       esi,0FFFFFFFF
       call      qword ptr [7F8F41706D90]; System.Threading.Tasks.Task.InternalWaitCore(Int32, System.Threading.CancellationToken)
M02_L00:
       test      dword ptr [rbx+34],10000000
       jne       short M02_L03
M02_L01:
       mov       edi,[rbx+34]
       and       edi,1600000
       cmp       edi,1000000
       jne       short M02_L04
M02_L02:
       pop       rbx
       pop       r15
       pop       rbp
       ret
M02_L03:
       mov       rdi,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       test      eax,eax
       je        short M02_L01
       mov       rdi,rbx
       call      qword ptr [7F8F418CDB30]
       jmp       short M02_L01
M02_L04:
       test      r15b,2
       jne       short M02_L05
       mov       rdi,rbx
       call      qword ptr [7F8F418CE3D0]
M02_L05:
       mov       rdi,rbx
       call      qword ptr [7F8F418CFFC0]
       jmp       short M02_L02
; Total bytes of code 125
```

## .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2
```assembly
; Chrysalis.Cbor.Benchmark.Benchmarks.Deserializers.AlonzoBenchmarks.Old()
       push      rbp
       sub       rsp,20
       lea       rbp,[rsp+20]
       vxorps    xmm8,xmm8,xmm8
       vmovdqu   ymmword ptr [rbp-20],ymm8
       mov       [rbp-20],rdi
       mov       dword ptr [rbp-18],0FFFFFFFF
       lea       rdi,[rbp-20]
       call      qword ptr [7FA2850D4B40]
       mov       rax,[rbp-10]
       test      rax,rax
       je        short M00_L01
M00_L00:
       add       rsp,20
       pop       rbp
       ret
M00_L01:
       lea       rdi,[rbp-10]
       call      qword ptr [7FA286035290]
       jmp       short M00_L00
; Total bytes of code 68
```
```assembly
; BenchmarkDotNet.Helpers.AwaitHelper.GetResult(System.Threading.Tasks.Task)
       push      rax
       mov       esi,[rdi+34]
       and       esi,11000000
       cmp       esi,1000000
       jne       short M01_L01
M01_L00:
       add       rsp,8
       ret
M01_L01:
       xor       esi,esi
       call      qword ptr [7FA2850D6AC0]; System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task, System.Threading.Tasks.ConfigureAwaitOptions)
       jmp       short M01_L00
; Total bytes of code 33
```
```assembly
; System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task, System.Threading.Tasks.ConfigureAwaitOptions)
       push      rbp
       push      r15
       push      rbx
       lea       rbp,[rsp+10]
       mov       rbx,rdi
       mov       r15d,esi
       test      dword ptr [rbx+34],1600000
       jne       short M02_L00
       mov       rdi,rbx
       xor       edx,edx
       mov       esi,0FFFFFFFF
       call      qword ptr [7FA2850D6AD8]; System.Threading.Tasks.Task.InternalWaitCore(Int32, System.Threading.CancellationToken)
M02_L00:
       test      dword ptr [rbx+34],10000000
       jne       short M02_L03
M02_L01:
       mov       edi,[rbx+34]
       and       edi,1600000
       cmp       edi,1000000
       jne       short M02_L04
M02_L02:
       pop       rbx
       pop       r15
       pop       rbp
       ret
M02_L03:
       mov       rdi,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       test      eax,eax
       je        short M02_L01
       mov       rdi,rbx
       call      qword ptr [7FA286034B58]
       jmp       short M02_L01
M02_L04:
       test      r15b,2
       jne       short M02_L05
       mov       rdi,rbx
       call      qword ptr [7FA2860352A8]
M02_L05:
       mov       rdi,rbx
       call      qword ptr [7FA286035A40]
       jmp       short M02_L02
; Total bytes of code 125
```

