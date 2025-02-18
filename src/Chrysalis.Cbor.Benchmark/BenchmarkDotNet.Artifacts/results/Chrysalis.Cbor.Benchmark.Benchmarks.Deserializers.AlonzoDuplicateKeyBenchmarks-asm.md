## .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2
```assembly
; Chrysalis.Cbor.Benchmark.Benchmarks.Deserializers.AlonzoDuplicateKeyBenchmarks.New()
       push      rbp
       sub       rsp,20
       lea       rbp,[rsp+20]
       vxorps    xmm8,xmm8,xmm8
       vmovdqu   ymmword ptr [rbp-20],ymm8
       mov       [rbp-20],rdi
       mov       dword ptr [rbp-18],0FFFFFFFF
       lea       rdi,[rbp-20]
       call      qword ptr [7F0358B34BB8]
       mov       rax,[rbp-10]
       test      rax,rax
       je        short M00_L01
M00_L00:
       add       rsp,20
       pop       rbp
       ret
M00_L01:
       lea       rdi,[rbp-10]
       call      qword ptr [7F0358D4C0F0]
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
       call      qword ptr [7F0358B36C88]; System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task, System.Threading.Tasks.ConfigureAwaitOptions)
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
       call      qword ptr [7F0358B36CA0]; System.Threading.Tasks.Task.InternalWaitCore(Int32, System.Threading.CancellationToken)
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
       call      qword ptr [7F0358D478D0]
       jmp       short M02_L01
M02_L04:
       test      r15b,2
       jne       short M02_L05
       mov       rdi,rbx
       call      qword ptr [7F0358D4C108]
M02_L05:
       mov       rdi,rbx
       call      qword ptr [7F0358D4C7F8]
       jmp       short M02_L02
; Total bytes of code 125
```

## .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2
```assembly
; Chrysalis.Cbor.Benchmark.Benchmarks.Deserializers.AlonzoDuplicateKeyBenchmarks.Old()
       push      rbp
       sub       rsp,20
       lea       rbp,[rsp+20]
       vxorps    xmm8,xmm8,xmm8
       vmovdqu   ymmword ptr [rbp-20],ymm8
       mov       [rbp-20],rdi
       mov       dword ptr [rbp-18],0FFFFFFFF
       lea       rdi,[rbp-20]
       call      qword ptr [7F34E8524BB8]
       mov       rax,[rbp-10]
       test      rax,rax
       je        short M00_L01
M00_L00:
       add       rsp,20
       pop       rbp
       ret
M00_L01:
       lea       rdi,[rbp-10]
       call      qword ptr [7F34E9485140]
       jmp       short M00_L00
; Total bytes of code 68
```
```assembly
; BenchmarkDotNet.Helpers.AwaitHelper.GetResult(System.Threading.Tasks.Task)
       push      rbp
       sub       rsp,10
       lea       rbp,[rsp+10]
       xor       eax,eax
       mov       [rbp-10],rax
       mov       [rbp-8],rdi
       mov       rdi,[rbp-8]
       cmp       [rdi],edi
       call      qword ptr [7F34E8524FD8]; System.Threading.Tasks.Task.GetAwaiter()
       mov       [rbp-10],rax
       lea       rdi,[rbp-10]
       call      qword ptr [7F34E8525008]; System.Runtime.CompilerServices.TaskAwaiter.GetResult()
       nop
       add       rsp,10
       pop       rbp
       ret
; Total bytes of code 53
```
```assembly
; System.Threading.Tasks.Task.GetAwaiter()
       mov       rax,rdi
       ret
; Total bytes of code 4
```
```assembly
; System.Runtime.CompilerServices.TaskAwaiter.GetResult()
       mov       rdi,[rdi]
       mov       esi,[rdi+34]
       and       esi,11000000
       cmp       esi,1000000
       jne       short M03_L00
       ret
M03_L00:
       xor       esi,esi
       jmp       qword ptr [7F34E8526B38]; System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task, System.Threading.Tasks.ConfigureAwaitOptions)
; Total bytes of code 29
```

